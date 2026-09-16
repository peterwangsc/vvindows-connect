#define XR_USE_PLATFORM_WIN32
#define XR_USE_GRAPHICS_API_D3D11
#include "capture.h"
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <atomic>
#include <chrono>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

XR_DEFINE_HANDLE(XrOpaqueDataChannelNV)
#define XR_TYPE_OPAQUE_DATA_CHANNEL_CREATE_INFO_NV ((XrStructureType)1000500000)
#define XR_TYPE_OPAQUE_DATA_CHANNEL_STATE_NV ((XrStructureType)1000500001)
enum XrOpaqueDataChannelStatusNV { CH_CONNECTING = 0, CH_CONNECTED = 1, CH_SHUTTING = 2, CH_DISCONNECTED = 3 };
struct XrGuidNV { uint32_t d1; uint16_t d2, d3; uint8_t d4[8]; };
struct XrOpaqueDataChannelCreateInfoNV { XrStructureType type; const void* next; XrSystemId systemId; XrGuidNV uuid; };
struct XrOpaqueDataChannelStateNV { XrStructureType type; void* next; XrOpaqueDataChannelStatusNV state; };
typedef XrResult(XRAPI_PTR* PFN_create)(XrInstance, const XrOpaqueDataChannelCreateInfoNV*, XrOpaqueDataChannelNV*);
typedef XrResult(XRAPI_PTR* PFN_destroy)(XrOpaqueDataChannelNV);
typedef XrResult(XRAPI_PTR* PFN_state)(XrOpaqueDataChannelNV, XrOpaqueDataChannelStateNV*);
typedef XrResult(XRAPI_PTR* PFN_shutdown)(XrOpaqueDataChannelNV);
typedef XrResult(XRAPI_PTR* PFN_send)(XrOpaqueDataChannelNV, uint32_t, const uint8_t*);

typedef void(__stdcall* EventFn)(int32_t kind, int32_t value);
enum { EV_STAGE = 1, EV_SESSION = 2, EV_CHANNEL = 3, EV_SENT = 4, EV_ERROR = 5, EV_EXIT = 6 };
enum { ST_INSTANCE = 1, ST_SYSTEM, ST_D3D, ST_SESSION, ST_SWAPCHAIN, ST_CHANNEL, ST_LOOP, ST_CAPTURE };

static std::atomic<bool> g_quit{ false }, g_recenter{ false };
static std::thread g_thread;
static std::mutex g_lifecycle;
#include <cmath>

struct Swap { XrSwapchain handle; int32_t w, h; std::vector<XrSwapchainImageD3D11KHR> images; std::vector<ID3D11RenderTargetView*> rtvs; };

static bool makeSwap(XrSession sess, ID3D11Device* dev, uint32_t w, uint32_t h, uint32_t samples, Swap& s, XrResult& r) {
	XrSwapchainCreateInfo si = { XR_TYPE_SWAPCHAIN_CREATE_INFO };
	si.arraySize = si.mipCount = si.faceCount = 1; si.format = DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
	si.width = w; si.height = h; si.sampleCount = samples;
	si.usageFlags = XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
	s = {}; s.w = w; s.h = h;
	if (XR_FAILED(r = xrCreateSwapchain(sess, &si, &s.handle))) return false;
	uint32_t ic = 0; xrEnumerateSwapchainImages(s.handle, 0, &ic, nullptr);
	s.images.resize(ic, { XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR });
	xrEnumerateSwapchainImages(s.handle, ic, &ic, (XrSwapchainImageBaseHeader*)s.images.data());
	for (auto& im : s.images) {
		D3D11_RENDER_TARGET_VIEW_DESC rd = {}; rd.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D; rd.Format = DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
		ID3D11RenderTargetView* rtv = nullptr; dev->CreateRenderTargetView(im.texture, &rd, &rtv); s.rtvs.push_back(rtv);
	}
	return true;
}

static void run(std::wstring runtimeJson, std::string paired, float quadWidth, float quadDistance, EventFn ev) {
	SetEnvironmentVariableW(L"XR_RUNTIME_JSON", runtimeJson.c_str());
	auto fail = [&](int stage, XrResult r) { ev(EV_ERROR, stage * 100000 + (int32_t)r); };
	XrInstance inst = XR_NULL_HANDLE; XrSession sess = XR_NULL_HANDLE; XrSpace space = XR_NULL_HANDLE;
	ID3D11Device* dev = nullptr; ID3D11DeviceContext* ctx = nullptr;
	XrOpaqueDataChannelNV chan = XR_NULL_HANDLE; PFN_destroy pDestroy = nullptr; PFN_shutdown pShutdown = nullptr;
	std::vector<Swap> swaps; Swap quad{}; std::unique_ptr<Capture> cap;
	const bool quadMode = quadWidth > 0;
	XrSessionState state = XR_SESSION_STATE_UNKNOWN; bool running = false, sent = paired.empty();
	auto cleanup = [&]() {
		if (chan && pShutdown) pShutdown(chan);
		if (chan && pDestroy) pDestroy(chan);
		for (auto& s : swaps) { for (auto* v : s.rtvs) v->Release(); xrDestroySwapchain(s.handle); }
		if (quad.handle) { for (auto* v : quad.rtvs) v->Release(); xrDestroySwapchain(quad.handle); }
		cap.reset();
		if (space) xrDestroySpace(space);
		if (sess) xrDestroySession(sess);
		if (inst) xrDestroyInstance(inst);
		if (ctx) ctx->Release();
		if (dev) dev->Release();
		ev(EV_EXIT, 0);
	};
	ev(EV_STAGE, ST_INSTANCE);
	uint32_t n = 0; xrEnumerateInstanceExtensionProperties(nullptr, 0, &n, nullptr);
	std::vector<XrExtensionProperties> exts(n, { XR_TYPE_EXTENSION_PROPERTIES });
	xrEnumerateInstanceExtensionProperties(nullptr, n, &n, exts.data());
	const char* want[] = { XR_KHR_D3D11_ENABLE_EXTENSION_NAME, "XR_NVX1_opaque_data_channel", "XR_NV_opaque_data_channel" };
	std::vector<const char*> use;
	for (auto& e : exts) for (auto w : want) if (!strcmp(w, e.extensionName)) use.push_back(w);
	XrInstanceCreateInfo ici = { XR_TYPE_INSTANCE_CREATE_INFO };
	ici.enabledExtensionCount = (uint32_t)use.size(); ici.enabledExtensionNames = use.data();
	ici.applicationInfo.apiVersion = XR_CURRENT_API_VERSION;
	strcpy_s(ici.applicationInfo.applicationName, "vindOS");
	XrResult r; auto until = std::chrono::steady_clock::now() + std::chrono::seconds(90);
	auto retry = [&](auto call) { while (XR_FAILED(r = call()) && !g_quit && std::chrono::steady_clock::now() < until) std::this_thread::sleep_for(std::chrono::milliseconds(250)); return XR_SUCCEEDED(r); };
	if (!retry([&] { return xrCreateInstance(&ici, &inst); })) { fail(ST_INSTANCE, r); return cleanup(); }
	PFN_xrGetD3D11GraphicsRequirementsKHR pReq = nullptr; PFN_create pCreate = nullptr; PFN_state pState = nullptr; PFN_send pSend = nullptr;
	xrGetInstanceProcAddr(inst, "xrGetD3D11GraphicsRequirementsKHR", (PFN_xrVoidFunction*)&pReq);
	xrGetInstanceProcAddr(inst, "xrCreateOpaqueDataChannelNV", (PFN_xrVoidFunction*)&pCreate);
	xrGetInstanceProcAddr(inst, "xrDestroyOpaqueDataChannelNV", (PFN_xrVoidFunction*)&pDestroy);
	xrGetInstanceProcAddr(inst, "xrGetOpaqueDataChannelStateNV", (PFN_xrVoidFunction*)&pState);
	xrGetInstanceProcAddr(inst, "xrShutdownOpaqueDataChannelNV", (PFN_xrVoidFunction*)&pShutdown);
	xrGetInstanceProcAddr(inst, "xrSendOpaqueDataChannelNV", (PFN_xrVoidFunction*)&pSend);
	ev(EV_STAGE, ST_SYSTEM);
	XrSystemGetInfo sgi = { XR_TYPE_SYSTEM_GET_INFO }; sgi.formFactor = XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
	XrSystemId sys = XR_NULL_SYSTEM_ID;
	if (!retry([&] { return xrGetSystem(inst, &sgi, &sys); })) { fail(ST_SYSTEM, r); return cleanup(); }
	XrGraphicsRequirementsD3D11KHR req = { XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR };
	if (!pReq || XR_FAILED(r = pReq(inst, sys, &req))) { fail(ST_D3D, r); return cleanup(); }
	ev(EV_STAGE, ST_D3D);
	IDXGIFactory1* fac = nullptr; IDXGIAdapter1* ad = nullptr; IDXGIAdapter1* pick = nullptr;
	CreateDXGIFactory1(__uuidof(IDXGIFactory1), (void**)&fac);
	for (UINT i = 0; fac->EnumAdapters1(i, &ad) == S_OK; i++) {
		DXGI_ADAPTER_DESC1 d; ad->GetDesc1(&d);
		if (!memcmp(&d.AdapterLuid, &req.adapterLuid, sizeof(LUID))) { pick = ad; break; }
		ad->Release();
	}
	fac->Release();
	D3D_FEATURE_LEVEL fl = D3D_FEATURE_LEVEL_11_0;
	if (!pick || FAILED(D3D11CreateDevice(pick, D3D_DRIVER_TYPE_UNKNOWN, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, &fl, 1, D3D11_SDK_VERSION, &dev, nullptr, &ctx))) { fail(ST_D3D, XR_ERROR_RUNTIME_FAILURE); if (pick) pick->Release(); return cleanup(); }
	pick->Release();
	if (quadMode) {
		ev(EV_STAGE, ST_CAPTURE);
		try { cap = std::make_unique<Capture>(); cap->init(dev, ctx); } catch (CaptureError e) { fail(ST_CAPTURE, (XrResult)(e.hr & 0xFFFF)); return cleanup(); }
	}
	ev(EV_STAGE, ST_SESSION);
	XrGraphicsBindingD3D11KHR bind = { XR_TYPE_GRAPHICS_BINDING_D3D11_KHR }; bind.device = dev;
	XrSessionCreateInfo sci = { XR_TYPE_SESSION_CREATE_INFO }; sci.next = &bind; sci.systemId = sys;
	if (!retry([&] { return xrCreateSession(inst, &sci, &sess); })) { fail(ST_SESSION, r); return cleanup(); }
	XrReferenceSpaceCreateInfo rsci = { XR_TYPE_REFERENCE_SPACE_CREATE_INFO };
	rsci.poseInReferenceSpace = { {0, 0, 0, 1}, {0, 0, 0} }; rsci.referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL;
	xrCreateReferenceSpace(sess, &rsci, &space);
	ev(EV_STAGE, ST_SWAPCHAIN);
	const XrViewConfigurationType vct = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
	uint32_t vc = 0; xrEnumerateViewConfigurationViews(inst, sys, vct, 0, &vc, nullptr);
	std::vector<XrViewConfigurationView> cfg(vc, { XR_TYPE_VIEW_CONFIGURATION_VIEW });
	xrEnumerateViewConfigurationViews(inst, sys, vct, vc, &vc, cfg.data());
	std::vector<XrView> views(vc, { XR_TYPE_VIEW });
	for (auto& v : cfg) { Swap s; if (!makeSwap(sess, dev, v.recommendedImageRectWidth, v.recommendedImageRectHeight, v.recommendedSwapchainSampleCount, s, r)) { fail(ST_SWAPCHAIN, r); return cleanup(); } swaps.push_back(s); }
	if (quadMode && !makeSwap(sess, dev, cap->w, cap->h, 1, quad, r)) { fail(ST_SWAPCHAIN, r); return cleanup(); }
	ev(EV_STAGE, ST_CHANNEL);
	if (!paired.empty()) {
		if (pCreate) {
			XrOpaqueDataChannelCreateInfoNV cci = { XR_TYPE_OPAQUE_DATA_CHANNEL_CREATE_INFO_NV, nullptr, sys, { 0x76696e64, 0x4f53, 0x0001, {0x76,0x69,0x6e,0x64,0x4f,0x53,0x00,0x01} } };
			if (XR_FAILED(r = pCreate(inst, &cci, &chan))) { fail(ST_CHANNEL, r); chan = XR_NULL_HANDLE; }
		} else fail(ST_CHANNEL, XR_ERROR_EXTENSION_NOT_PRESENT);
	}
	ev(EV_STAGE, ST_LOOP);
	int chanState = -1; auto lastPoll = std::chrono::steady_clock::now();
	const float clear[4] = { 0.f, 0.f, 0.f, 1.f };
	XrPosef quadPose = { {0, 0, 0, 1}, {0, 0, -quadDistance} }; bool quadPlaced = false; std::chrono::steady_clock::time_point visibleSince{};
	while (!g_quit) {
		XrEventDataBuffer eb = { XR_TYPE_EVENT_DATA_BUFFER };
		while (xrPollEvent(inst, &eb) == XR_SUCCESS) {
			if (eb.type == XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED) {
				state = ((XrEventDataSessionStateChanged*)&eb)->state; ev(EV_SESSION, state);
				if (state == XR_SESSION_STATE_READY) { XrSessionBeginInfo bi = { XR_TYPE_SESSION_BEGIN_INFO }; bi.primaryViewConfigurationType = vct; running = XR_SUCCEEDED(xrBeginSession(sess, &bi)); }
				else if (state == XR_SESSION_STATE_STOPPING) { running = false; xrEndSession(sess); }
				else if (state == XR_SESSION_STATE_EXITING || state == XR_SESSION_STATE_LOSS_PENDING) g_quit = true;
			} else if (eb.type == XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING) g_quit = true;
			eb = { XR_TYPE_EVENT_DATA_BUFFER };
		}
		if (g_quit) break;
		if (chan && std::chrono::steady_clock::now() - lastPoll > std::chrono::milliseconds(100)) {
			lastPoll = std::chrono::steady_clock::now();
			XrOpaqueDataChannelStateNV cs = { XR_TYPE_OPAQUE_DATA_CHANNEL_STATE_NV };
			if (XR_SUCCEEDED(pState(chan, &cs)) && cs.state != chanState) { chanState = cs.state; ev(EV_CHANNEL, chanState); }
		}
		bool visible = state == XR_SESSION_STATE_VISIBLE || state == XR_SESSION_STATE_FOCUSED;
		if (!sent && visible && chanState == CH_CONNECTED) { r = pSend(chan, (uint32_t)paired.size(), (const uint8_t*)paired.data()); sent = XR_SUCCEEDED(r); ev(sent ? EV_SENT : EV_ERROR, sent ? 1 : ST_CHANNEL * 100000 + r); }
		if (!running) { std::this_thread::sleep_for(std::chrono::milliseconds(10)); continue; }
		XrFrameState fs = { XR_TYPE_FRAME_STATE };
		xrWaitFrame(sess, nullptr, &fs); xrBeginFrame(sess, nullptr);
		std::vector<XrCompositionLayerProjectionView> pv;
		XrCompositionLayerProjection layer = { XR_TYPE_COMPOSITION_LAYER_PROJECTION };
		XrCompositionLayerQuad qlayer = { XR_TYPE_COMPOSITION_LAYER_QUAD };
		XrCompositionLayerBaseHeader* layers[2] = { nullptr, nullptr }; uint32_t layerCount = 0;
		if (visible) {
			XrViewState vs = { XR_TYPE_VIEW_STATE }; XrViewLocateInfo li = { XR_TYPE_VIEW_LOCATE_INFO };
			li.viewConfigurationType = vct; li.displayTime = fs.predictedDisplayTime; li.space = space;
			uint32_t cnt = 0; xrLocateViews(sess, &li, &vs, vc, &cnt, views.data());
			if (quadMode && cnt > 0 && (vs.viewStateFlags & XR_VIEW_STATE_POSITION_VALID_BIT)) {
				if (visibleSince == std::chrono::steady_clock::time_point{}) visibleSince = std::chrono::steady_clock::now();
				bool due = !quadPlaced && std::chrono::steady_clock::now() - visibleSince > std::chrono::milliseconds(1500);
				if (due || g_recenter.exchange(false)) {
					XrVector3f head = { 0, 0, 0 }; for (uint32_t i = 0; i < cnt; i++) { head.x += views[i].pose.position.x / cnt; head.y += views[i].pose.position.y / cnt; head.z += views[i].pose.position.z / cnt; }
					XrQuaternionf q = views[0].pose.orientation;
					float fx = -(2 * (q.x * q.z + q.w * q.y)), fz = -(1 - 2 * (q.x * q.x + q.y * q.y));
					float len = std::sqrt(fx * fx + fz * fz); if (len < 1e-4f) { fx = 0; fz = -1; len = 1; } fx /= len; fz /= len;
					float yaw = std::atan2(-fx, -fz);
					quadPose.position = { head.x + fx * quadDistance, head.y, head.z + fz * quadDistance };
					quadPose.orientation = { 0, std::sin(yaw / 2), 0, std::cos(yaw / 2) };
					quadPlaced = true; ev(EV_STAGE, 100);
				}
			}
			pv.resize(cnt, { XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW });
			for (uint32_t i = 0; i < cnt; i++) {
				uint32_t idx = 0; XrSwapchainImageAcquireInfo ai = { XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
				xrAcquireSwapchainImage(swaps[i].handle, &ai, &idx);
				XrSwapchainImageWaitInfo wi = { XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO }; wi.timeout = XR_INFINITE_DURATION;
				xrWaitSwapchainImage(swaps[i].handle, &wi);
				ctx->ClearRenderTargetView(swaps[i].rtvs[idx], clear);
				XrSwapchainImageReleaseInfo ri = { XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
				xrReleaseSwapchainImage(swaps[i].handle, &ri);
				pv[i].pose = views[i].pose; pv[i].fov = views[i].fov;
				pv[i].subImage.swapchain = swaps[i].handle; pv[i].subImage.imageRect = { {0, 0}, {swaps[i].w, swaps[i].h} };
			}
			layer.space = space; layer.viewCount = cnt; layer.views = pv.data(); layers[layerCount++] = (XrCompositionLayerBaseHeader*)&layer;
			if (quadMode) {
				bool fresh = false; ID3D11Texture2D* tex = nullptr;
				try { tex = cap->acquire(0, fresh); } catch (CaptureError e) { fail(ST_CAPTURE, (XrResult)(e.hr & 0xFFFF)); g_quit = true; }
				if (tex) {
					uint32_t idx = 0; XrSwapchainImageAcquireInfo ai = { XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
					xrAcquireSwapchainImage(quad.handle, &ai, &idx);
					XrSwapchainImageWaitInfo wi = { XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO }; wi.timeout = XR_INFINITE_DURATION;
					xrWaitSwapchainImage(quad.handle, &wi);
					ctx->CopyResource(quad.images[idx].texture, tex);
					XrSwapchainImageReleaseInfo ri = { XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
					xrReleaseSwapchainImage(quad.handle, &ri);
					qlayer.space = space; qlayer.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
					qlayer.subImage.swapchain = quad.handle; qlayer.subImage.imageRect = { {0, 0}, {quad.w, quad.h} };
					qlayer.pose = quadPose;
					qlayer.size = { quadWidth, quadWidth * quad.h / quad.w };
					layers[layerCount++] = (XrCompositionLayerBaseHeader*)&qlayer;
				}
				cap->release();
			}
		}
		XrFrameEndInfo fe = { XR_TYPE_FRAME_END_INFO };
		fe.displayTime = fs.predictedDisplayTime; fe.environmentBlendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
		fe.layerCount = layerCount; fe.layers = layers;
		xrEndFrame(sess, &fe);
	}
	if (running) { xrRequestExitSession(sess); xrEndSession(sess); }
	cleanup();
}

extern "C" __declspec(dllexport) int32_t vindos_xr_start(const wchar_t* runtimeJson, const char* pairedJson, float quadWidth, float quadDistance, EventFn onEvent) {
	std::lock_guard<std::mutex> lock(g_lifecycle);
	if (g_thread.joinable()) return -1;
	g_quit = false;
	g_thread = std::thread(run, std::wstring(runtimeJson), std::string(pairedJson ? pairedJson : ""), quadWidth, quadDistance, onEvent);
	return 0;
}

extern "C" __declspec(dllexport) void vindos_xr_recenter() { g_recenter = true; }

extern "C" __declspec(dllexport) void vindos_xr_stop() {
	std::lock_guard<std::mutex> lock(g_lifecycle);
	g_quit = true;
	if (g_thread.joinable()) g_thread.join();
}
