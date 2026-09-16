#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d3dcompiler.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <strmif.h>
#include <codecapi.h>
#include <wrl/client.h>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <vector>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "mf.lib")
#pragma comment(lib, "ole32.lib")

using Microsoft::WRL::ComPtr;
typedef void(__stdcall* FrameFn)(int64_t pts, int32_t flags, const uint8_t* data, int32_t length);
typedef void(__stdcall* DesktopEventFn)(int32_t code, int32_t value);

static std::atomic<bool> d_quit{ false }, d_idr{ false };
static std::thread d_thread;
static RECT d_rect{};

static void check(HRESULT hr, int stage) { if (FAILED(hr)) throw std::pair<int, HRESULT>(stage, hr); }

struct Grabber final : IMFSampleGrabberSinkCallback {
	std::atomic<ULONG> refs{ 1 }; FrameFn cb; std::vector<uint8_t> out, sps, pps;
	explicit Grabber(FrameFn f) : cb(f) {}
	HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** r) override {
		if (!r) return E_POINTER;
		if (id == __uuidof(IUnknown) || id == __uuidof(IMFClockStateSink) || id == __uuidof(IMFSampleGrabberSinkCallback)) { *r = static_cast<IMFSampleGrabberSinkCallback*>(this); AddRef(); return S_OK; }
		*r = nullptr; return E_NOINTERFACE;
	}
	ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
	ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
	HRESULT STDMETHODCALLTYPE OnSetPresentationClock(IMFPresentationClock*) override { return S_OK; }
	HRESULT STDMETHODCALLTYPE OnShutdown() override { return S_OK; }
	HRESULT STDMETHODCALLTYPE OnClockStart(MFTIME, LONGLONG) override { return S_OK; }
	HRESULT STDMETHODCALLTYPE OnClockStop(MFTIME) override { return S_OK; }
	HRESULT STDMETHODCALLTYPE OnClockPause(MFTIME) override { return S_OK; }
	HRESULT STDMETHODCALLTYPE OnClockRestart(MFTIME) override { return S_OK; }
	HRESULT STDMETHODCALLTYPE OnClockSetRate(MFTIME, float) override { return S_OK; }
	HRESULT STDMETHODCALLTYPE OnProcessSample(REFGUID, DWORD, LONGLONG time, LONGLONG, const BYTE* data, DWORD length) override {
		std::vector<std::pair<const BYTE*, size_t>> nals;
		size_t i = 0, start = SIZE_MAX;
		while (i + 3 <= length) {
			if (data[i] == 0 && data[i + 1] == 0 && (data[i + 2] == 1 || (data[i + 2] == 0 && i + 3 < length && data[i + 3] == 1))) {
				size_t sc = data[i + 2] == 1 ? 3 : 4;
				if (start != SIZE_MAX) { size_t end = i; while (end > start && data[end - 1] == 0) end--; nals.push_back({ data + start, end - start }); }
				i += sc; start = i;
			} else i++;
		}
		if (start != SIZE_MAX && start < length) nals.push_back({ data + start, length - start });
		bool idr = false, hasSps = false, hasPps = false;
		for (auto& n : nals) { int t = n.first[0] & 0x1f; idr |= t == 5; if (t == 7) { hasSps = true; sps.assign(n.first, n.first + n.second); } if (t == 8) { hasPps = true; pps.assign(n.first, n.first + n.second); } }
		out.clear();
		auto put = [&](const uint8_t* p, size_t n) { out.insert(out.end(), { 0, 0, 0, 1 }); out.insert(out.end(), p, p + n); };
		if (idr && !hasSps && !sps.empty()) put(sps.data(), sps.size());
		if (idr && !hasPps && !pps.empty()) put(pps.data(), pps.size());
		for (auto& n : nals) put(n.first, n.second);
		if (!out.empty() && out.size() <= 16u << 20) cb(time / 10, idr ? 1 : 0, out.data(), (int32_t)out.size());
		return S_OK;
	}
};

static const char* cursorShader = R"(
Texture2D<float4> desktop : register(t0);
Texture2D<uint2> pointerImage : register(t1);
cbuffer Placement : register(b0) { int2 origin; uint2 size; uint kind; uint3 padding; };
float4 vs(uint id : SV_VertexID) : SV_Position { float2 uv = float2((id << 1) & 2, id & 2); return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); }
float4 ps(float4 sp : SV_Position) : SV_Target {
	int2 p = int2(sp.xy); float4 bg = desktop.Load(int3(p, 0)); int2 q = p - origin;
	if (any(q < 0) || any(q >= int2(size))) return bg;
	uint2 d = pointerImage.Load(int3(q, 0));
	uint3 rgb = uint3((d.x >> 16) & 255, (d.x >> 8) & 255, d.x & 255);
	uint3 base = uint3(round(saturate(bg.rgb) * 255));
	if (kind == 1) return float4(float3((base & d.y) ^ rgb) / 255, 1);
	if (kind == 4) return float4(float3((d.x >> 24) == 255 ? base ^ rgb : rgb) / 255, 1);
	return float4(lerp(bg.rgb, float3(rgb) / 255, float(d.x >> 24) / 255), 1);
})";

static void run(uint32_t fps, uint32_t bitrate, FrameFn frame, DesktopEventFn ev) {
	check(CoInitializeEx(nullptr, COINIT_MULTITHREADED), 1);
	try {
		check(MFStartup(MF_VERSION), 1);
		ComPtr<ID3D11Device> dev; ComPtr<ID3D11DeviceContext> ctx;
		check(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &dev, nullptr, &ctx), 2);
		ComPtr<ID3D10Multithread> mt; check(ctx.As(&mt), 2); mt->SetMultithreadProtected(TRUE);
		ComPtr<IDXGIDevice> dxgi; check(dev.As(&dxgi), 3); ComPtr<IDXGIAdapter> adapter; check(dxgi->GetAdapter(&adapter), 3);
		ComPtr<IDXGIOutput> output; check(adapter->EnumOutputs(0, &output), 3); ComPtr<IDXGIOutput1> output1; check(output.As(&output1), 3);
		DXGI_OUTPUT_DESC od{}; output->GetDesc(&od); d_rect = od.DesktopCoordinates;
		ComPtr<IDXGIOutputDuplication> dup; check(output1->DuplicateOutput(dev.Get(), &dup), 3);
		DXGI_OUTDUPL_DESC dd; dup->GetDesc(&dd);
		const UINT w = dd.ModeDesc.Width & ~1u, h = dd.ModeDesc.Height & ~1u;
		ev(1, (int32_t)(w << 16 | h));
		ComPtr<ID3D11VideoDevice> vdev; ComPtr<ID3D11VideoContext> vctx; check(dev.As(&vdev), 4); check(ctx.As(&vctx), 4);
		D3D11_VIDEO_PROCESSOR_CONTENT_DESC vd{}; vd.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE; vd.InputWidth = vd.OutputWidth = w; vd.InputHeight = vd.OutputHeight = h; vd.InputFrameRate = vd.OutputFrameRate = { fps, 1 }; vd.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
		ComPtr<ID3D11VideoProcessorEnumerator> ve; check(vdev->CreateVideoProcessorEnumerator(&vd, &ve), 4);
		ComPtr<ID3D11VideoProcessor> vp; check(vdev->CreateVideoProcessor(ve.Get(), 0, &vp), 4);
		ComPtr<IMFDXGIDeviceManager> mgr; UINT token = 0; check(MFCreateDXGIDeviceManager(&token, &mgr), 5); check(mgr->ResetDevice(dev.Get(), token), 5);
		ComPtr<IMFAttributes> attrs; check(MFCreateAttributes(&attrs, 4), 5);
		attrs->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE); attrs->SetUINT32(MF_LOW_LATENCY, TRUE); attrs->SetUnknown(MF_SINK_WRITER_D3D_MANAGER, mgr.Get());
		ComPtr<IMFMediaType> enc; check(MFCreateMediaType(&enc), 5);
		enc->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); enc->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264); enc->SetUINT32(MF_MT_AVG_BITRATE, bitrate); enc->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
		MFSetAttributeSize(enc.Get(), MF_MT_FRAME_SIZE, w, h); MFSetAttributeRatio(enc.Get(), MF_MT_FRAME_RATE, fps, 1); MFSetAttributeRatio(enc.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
		ComPtr<Grabber> grabber; grabber.Attach(new Grabber(frame));
		ComPtr<IMFActivate> act; check(MFCreateSampleGrabberSinkActivate(enc.Get(), grabber.Get(), &act), 5);
		act->SetUINT32(MF_SAMPLEGRABBERSINK_IGNORE_CLOCK, TRUE);
		ComPtr<IMFMediaSink> sink; check(act->ActivateObject(IID_PPV_ARGS(&sink)), 5);
		ComPtr<IMFSinkWriter> writer; check(MFCreateSinkWriterFromMediaSink(sink.Get(), attrs.Get(), &writer), 5);
		ComPtr<IMFStreamSink> ss; check(sink->GetStreamSinkByIndex(0, &ss), 5); DWORD stream = 0; check(ss->GetIdentifier(&stream), 5);
		ComPtr<IMFMediaType> raw; check(MFCreateMediaType(&raw), 5);
		raw->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); raw->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12); raw->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
		MFSetAttributeSize(raw.Get(), MF_MT_FRAME_SIZE, w, h); MFSetAttributeRatio(raw.Get(), MF_MT_FRAME_RATE, fps, 1); MFSetAttributeRatio(raw.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
		check(writer->SetInputMediaType(stream, raw.Get(), nullptr), 5);
		ComPtr<ICodecAPI> codec; ComPtr<IMFSinkWriterEx> wex; check(writer.As(&wex), 6);
		for (DWORD i = 0; i < 8 && !codec; i++) {
			GUID cat; ComPtr<IMFTransform> t; if (FAILED(wex->GetTransformForStream(stream, i, &cat, &t))) break;
			if (cat == MFT_CATEGORY_VIDEO_ENCODER) t.As(&codec);
		}
		if (!codec) throw std::pair<int, HRESULT>(6, E_FAIL);
		auto set = [&](const GUID& g, ULONG v, bool b) { VARIANT x; VariantInit(&x); if (b) { x.vt = VT_BOOL; x.boolVal = v ? VARIANT_TRUE : VARIANT_FALSE; } else { x.vt = VT_UI4; x.ulVal = v; } codec->SetValue(&g, &x); };
		set(CODECAPI_AVLowLatencyMode, 1, true); set(CODECAPI_AVEncMPVDefaultBPictureCount, 0, false); set(CODECAPI_AVEncMPVGOPSize, fps * 2, false);
		set(CODECAPI_AVEncCommonRateControlMode, eAVEncCommonRateControlMode_CBR, false); set(CODECAPI_AVEncCommonMeanBitRate, bitrate, false);
		check(writer->BeginWriting(), 6);
		ComPtr<ID3D11VertexShader> vs; ComPtr<ID3D11PixelShader> ps; ComPtr<ID3DBlob> blob, errors;
		check(D3DCompile(cursorShader, strlen(cursorShader), nullptr, nullptr, nullptr, "vs", "vs_5_0", 0, 0, &blob, &errors), 7);
		check(dev->CreateVertexShader(blob->GetBufferPointer(), blob->GetBufferSize(), nullptr, &vs), 7); blob.Reset();
		check(D3DCompile(cursorShader, strlen(cursorShader), nullptr, nullptr, nullptr, "ps", "ps_5_0", 0, 0, &blob, &errors), 7);
		check(dev->CreatePixelShader(blob->GetBufferPointer(), blob->GetBufferSize(), nullptr, &ps), 7);
		D3D11_TEXTURE2D_DESC td{}; td.Width = w; td.Height = h; td.MipLevels = td.ArraySize = 1; td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1; td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
		ComPtr<ID3D11Texture2D> background, composed, last; ComPtr<ID3D11ShaderResourceView> backgroundView; ComPtr<ID3D11RenderTargetView> target;
		check(dev->CreateTexture2D(&td, nullptr, &background), 7); check(dev->CreateShaderResourceView(background.Get(), nullptr, &backgroundView), 7);
		td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE; check(dev->CreateTexture2D(&td, nullptr, &composed), 7); check(dev->CreateRenderTargetView(composed.Get(), nullptr, &target), 7);
		td.BindFlags = 0; check(dev->CreateTexture2D(&td, nullptr, &last), 7);
		D3D11_BUFFER_DESC bd{}; bd.ByteWidth = 32; bd.Usage = D3D11_USAGE_DEFAULT; bd.BindFlags = D3D11_BIND_CONSTANT_BUFFER; ComPtr<ID3D11Buffer> constants; check(dev->CreateBuffer(&bd, nullptr, &constants), 7);
		ComPtr<ID3D11Texture2D> pointer; ComPtr<ID3D11ShaderResourceView> pointerView; DXGI_OUTDUPL_POINTER_SHAPE_INFO shape{}; DXGI_OUTDUPL_POINTER_POSITION position{}; std::vector<BYTE> shapeBytes;
		td.Format = DXGI_FORMAT_NV12; td.BindFlags = D3D11_BIND_RENDER_TARGET; ComPtr<ID3D11Texture2D> nv12; check(dev->CreateTexture2D(&td, nullptr, &nv12), 7);
		D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC ov{}; ov.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D; ComPtr<ID3D11VideoProcessorOutputView> outView; check(vdev->CreateVideoProcessorOutputView(nv12.Get(), ve.Get(), &ov, &outView), 7);
		ComPtr<IMFMediaBuffer> buffer; check(MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D), nv12.Get(), 0, FALSE, &buffer), 7);
		ComPtr<IMF2DBuffer> b2; check(buffer.As(&b2), 7); DWORD blen = 0; check(b2->GetContiguousLength(&blen), 7); check(buffer->SetCurrentLength(blen), 7);
		ComPtr<IMFSample> sample; check(MFCreateSample(&sample), 7); check(sample->AddBuffer(buffer.Get()), 7);
		HANDLE timer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_MODIFY_STATE | SYNCHRONIZE);
		LARGE_INTEGER freq, now; QueryPerformanceFrequency(&freq); QueryPerformanceCounter(&now);
		const int64_t period = freq.QuadPart / fps, epoch = now.QuadPart; int64_t next = now.QuadPart, lastSubmit = 0; bool haveLast = false;
		ev(2, 1);
		while (!d_quit) {
			QueryPerformanceCounter(&now);
			if (next > now.QuadPart) { LARGE_INTEGER due; due.QuadPart = -((next - now.QuadPart) * 10000000 / freq.QuadPart); SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE); WaitForSingleObject(timer, 1000); QueryPerformanceCounter(&now); }
			next = now.QuadPart - next >= period ? now.QuadPart + period : next + period;
			DXGI_OUTDUPL_FRAME_INFO info{}; ComPtr<IDXGIResource> resource;
			HRESULT hr = dup->AcquireNextFrame(100, &info, &resource);
			ID3D11Texture2D* src = nullptr; ComPtr<ID3D11Texture2D> tex;
			if (hr == DXGI_ERROR_WAIT_TIMEOUT) {
				QueryPerformanceCounter(&now);
				if (!haveLast || (!d_idr && now.QuadPart - lastSubmit < freq.QuadPart)) continue;
				src = last.Get();
			} else {
				if (hr == DXGI_ERROR_ACCESS_LOST) throw std::pair<int, HRESULT>(8, hr);
				check(hr, 8);
				check(resource.As(&tex), 8);
				if (info.LastMouseUpdateTime.QuadPart) position = info.PointerPosition;
				if (info.PointerShapeBufferSize) {
					shapeBytes.resize(info.PointerShapeBufferSize); UINT need = 0;
					if (SUCCEEDED(dup->GetFramePointerShape((UINT)shapeBytes.size(), shapeBytes.data(), &need, &shape))) {
						const bool mono = shape.Type == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME; const UINT sh = mono ? shape.Height / 2 : shape.Height;
						if (shape.Width && sh && shape.Width <= 4096 && sh <= 4096) {
							std::vector<uint32_t> texels((size_t)shape.Width * sh * 2);
							for (UINT y = 0; y < sh; y++) for (UINT x = 0; x < shape.Width; x++) {
								size_t i = ((size_t)y * shape.Width + x) * 2;
								if (mono) { BYTE bit = (BYTE)(0x80 >> (x % 8)); texels[i] = (shapeBytes[(size_t)(y + sh) * shape.Pitch + x / 8] & bit) ? 0xFFFFFF : 0; texels[i + 1] = (shapeBytes[(size_t)y * shape.Pitch + x / 8] & bit) ? 0xFFFFFF : 0; }
								else memcpy(&texels[i], shapeBytes.data() + (size_t)y * shape.Pitch + x * 4, 4);
							}
							D3D11_TEXTURE2D_DESC pd{}; pd.Width = shape.Width; pd.Height = sh; pd.MipLevels = pd.ArraySize = 1; pd.Format = DXGI_FORMAT_R32G32_UINT; pd.SampleDesc.Count = 1; pd.Usage = D3D11_USAGE_IMMUTABLE; pd.BindFlags = D3D11_BIND_SHADER_RESOURCE;
							D3D11_SUBRESOURCE_DATA data{ texels.data(), shape.Width * 8, 0 };
							pointerView.Reset(); pointer.Reset();
							if (SUCCEEDED(dev->CreateTexture2D(&pd, &data, &pointer))) dev->CreateShaderResourceView(pointer.Get(), nullptr, &pointerView);
						}
					}
				}
				src = tex.Get();
				if (position.Visible && pointerView) {
					ctx->CopyResource(background.Get(), src);
					struct { INT x, y; UINT w, h, kind, pad[3]; } placement{ position.Position.x, position.Position.y, shape.Width, shape.Type == 1 ? shape.Height / 2 : shape.Height, shape.Type, {} };
					ctx->UpdateSubresource(constants.Get(), 0, nullptr, &placement, 0, 0);
					D3D11_VIEWPORT vpt{ 0, 0, (float)w, (float)h, 0, 1 }; ctx->RSSetViewports(1, &vpt);
					ctx->IASetInputLayout(nullptr); ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
					ctx->VSSetShader(vs.Get(), nullptr, 0); ctx->PSSetShader(ps.Get(), nullptr, 0);
					ID3D11Buffer* cb = constants.Get(); ctx->PSSetConstantBuffers(0, 1, &cb);
					ID3D11ShaderResourceView* views[] = { backgroundView.Get(), pointerView.Get() }; ctx->PSSetShaderResources(0, 2, views);
					ID3D11RenderTargetView* rt = target.Get(); ctx->OMSetRenderTargets(1, &rt, nullptr);
					ctx->Draw(3, 0);
					ID3D11ShaderResourceView* none[2] = {}; ctx->PSSetShaderResources(0, 2, none); ctx->OMSetRenderTargets(0, nullptr, nullptr);
					src = composed.Get();
				}
				ctx->CopyResource(last.Get(), src); haveLast = true;
			}
			ComPtr<ID3D11VideoProcessorInputView> in; D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC iv{}; iv.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
			check(vdev->CreateVideoProcessorInputView(src, ve.Get(), &iv, &in), 9);
			D3D11_VIDEO_PROCESSOR_STREAM vps{}; vps.Enable = TRUE; vps.pInputSurface = in.Get();
			check(vctx->VideoProcessorBlt(vp.Get(), outView.Get(), 0, 1, &vps), 9);
			if (hr != DXGI_ERROR_WAIT_TIMEOUT) dup->ReleaseFrame();
			ctx->Flush();
			if (d_idr.exchange(false)) set(CODECAPI_AVEncVideoForceKeyFrame, 1, false);
			QueryPerformanceCounter(&now); lastSubmit = now.QuadPart;
			sample->SetSampleTime((now.QuadPart - epoch) * 10000000 / freq.QuadPart); sample->SetSampleDuration(10000000 / fps);
			check(writer->WriteSample(stream, sample.Get()), 9);
		}
		CloseHandle(timer);
		writer->Finalize();
		MFShutdown();
		ev(3, 0);
	} catch (std::pair<int, HRESULT> e) { MFShutdown(); ev(4, e.first * 0x100000 + (int32_t)(e.second & 0xFFFFF)); }
	CoUninitialize();
}

extern "C" __declspec(dllexport) int32_t vindos_desktop_start(uint32_t fps, uint32_t bitrate, FrameFn onFrame, DesktopEventFn onEvent) {
	if (d_thread.joinable()) return -1;
	d_quit = false; d_idr = true;
	d_thread = std::thread(run, fps, bitrate, onFrame, onEvent);
	return 0;
}
extern "C" __declspec(dllexport) void vindos_desktop_idr() { d_idr = true; }
extern "C" __declspec(dllexport) void vindos_desktop_rect(int32_t* left, int32_t* top, int32_t* right, int32_t* bottom) { *left = d_rect.left; *top = d_rect.top; *right = d_rect.right; *bottom = d_rect.bottom; }
extern "C" __declspec(dllexport) void vindos_desktop_stop() { d_quit = true; if (d_thread.joinable()) d_thread.join(); }
