#include "capture.h"
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <strmif.h>
#include <codecapi.h>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <thread>
#include <vector>

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
static std::mutex d_lifecycle;
static RECT d_rect{};

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

static void run(uint32_t fps, uint32_t bitrate, FrameFn frame, DesktopEventFn ev) {
	ccheck(CoInitializeEx(nullptr, COINIT_MULTITHREADED), 1);
	try {
		ccheck(MFStartup(MF_VERSION), 1);
		ComPtr<ID3D11Device> dev; ComPtr<ID3D11DeviceContext> ctx;
		ccheck(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &dev, nullptr, &ctx), 2);
		ComPtr<ID3D10Multithread> mt; ccheck(ctx.As(&mt), 2); mt->SetMultithreadProtected(TRUE);
		Capture cap; cap.init(dev.Get(), ctx.Get()); d_rect = cap.rect;
		const UINT w = cap.w, h = cap.h;
		ev(1, (int32_t)(w << 16 | h));
		ComPtr<ID3D11VideoDevice> vdev; ComPtr<ID3D11VideoContext> vctx; ccheck(dev.As(&vdev), 4); ccheck(ctx.As(&vctx), 4);
		D3D11_VIDEO_PROCESSOR_CONTENT_DESC vd{}; vd.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE; vd.InputWidth = vd.OutputWidth = w; vd.InputHeight = vd.OutputHeight = h; vd.InputFrameRate = vd.OutputFrameRate = { fps, 1 }; vd.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
		ComPtr<ID3D11VideoProcessorEnumerator> ve; ccheck(vdev->CreateVideoProcessorEnumerator(&vd, &ve), 4);
		ComPtr<ID3D11VideoProcessor> vp; ccheck(vdev->CreateVideoProcessor(ve.Get(), 0, &vp), 4);
		ComPtr<IMFDXGIDeviceManager> mgr; UINT token = 0; ccheck(MFCreateDXGIDeviceManager(&token, &mgr), 5); ccheck(mgr->ResetDevice(dev.Get(), token), 5);
		ComPtr<IMFAttributes> attrs; ccheck(MFCreateAttributes(&attrs, 4), 5);
		attrs->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE); attrs->SetUINT32(MF_LOW_LATENCY, TRUE); attrs->SetUnknown(MF_SINK_WRITER_D3D_MANAGER, mgr.Get());
		ComPtr<IMFMediaType> enc; ccheck(MFCreateMediaType(&enc), 5);
		enc->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); enc->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264); enc->SetUINT32(MF_MT_AVG_BITRATE, bitrate); enc->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
		MFSetAttributeSize(enc.Get(), MF_MT_FRAME_SIZE, w, h); MFSetAttributeRatio(enc.Get(), MF_MT_FRAME_RATE, fps, 1); MFSetAttributeRatio(enc.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
		ComPtr<Grabber> grabber; grabber.Attach(new Grabber(frame));
		ComPtr<IMFActivate> act; ccheck(MFCreateSampleGrabberSinkActivate(enc.Get(), grabber.Get(), &act), 5);
		act->SetUINT32(MF_SAMPLEGRABBERSINK_IGNORE_CLOCK, TRUE);
		ComPtr<IMFMediaSink> sink; ccheck(act->ActivateObject(IID_PPV_ARGS(&sink)), 5);
		ComPtr<IMFSinkWriter> writer; ccheck(MFCreateSinkWriterFromMediaSink(sink.Get(), attrs.Get(), &writer), 5);
		ComPtr<IMFStreamSink> ss; ccheck(sink->GetStreamSinkByIndex(0, &ss), 5); DWORD stream = 0; ccheck(ss->GetIdentifier(&stream), 5);
		ComPtr<IMFMediaType> raw; ccheck(MFCreateMediaType(&raw), 5);
		raw->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); raw->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12); raw->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
		MFSetAttributeSize(raw.Get(), MF_MT_FRAME_SIZE, w, h); MFSetAttributeRatio(raw.Get(), MF_MT_FRAME_RATE, fps, 1); MFSetAttributeRatio(raw.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
		ccheck(writer->SetInputMediaType(stream, raw.Get(), nullptr), 5);
		ComPtr<ICodecAPI> codec; ComPtr<IMFSinkWriterEx> wex; ccheck(writer.As(&wex), 6);
		for (DWORD i = 0; i < 8 && !codec; i++) {
			GUID cat; ComPtr<IMFTransform> t; if (FAILED(wex->GetTransformForStream(stream, i, &cat, &t))) break;
			if (cat == MFT_CATEGORY_VIDEO_ENCODER) t.As(&codec);
		}
		if (!codec) throw CaptureError{ 6, E_FAIL };
		auto set = [&](const GUID& g, ULONG v, bool b) { VARIANT x; VariantInit(&x); if (b) { x.vt = VT_BOOL; x.boolVal = v ? VARIANT_TRUE : VARIANT_FALSE; } else { x.vt = VT_UI4; x.ulVal = v; } codec->SetValue(&g, &x); };
		set(CODECAPI_AVLowLatencyMode, 1, true); set(CODECAPI_AVEncMPVDefaultBPictureCount, 0, false); set(CODECAPI_AVEncMPVGOPSize, fps * 2, false);
		set(CODECAPI_AVEncCommonRateControlMode, eAVEncCommonRateControlMode_CBR, false); set(CODECAPI_AVEncCommonMeanBitRate, bitrate, false);
		ccheck(writer->BeginWriting(), 6);
		D3D11_TEXTURE2D_DESC td{}; td.Width = w; td.Height = h; td.MipLevels = td.ArraySize = 1; td.Format = DXGI_FORMAT_NV12; td.SampleDesc.Count = 1; td.BindFlags = D3D11_BIND_RENDER_TARGET;
		ComPtr<ID3D11Texture2D> nv12; ccheck(dev->CreateTexture2D(&td, nullptr, &nv12), 7);
		D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC ov{}; ov.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D; ComPtr<ID3D11VideoProcessorOutputView> outView; ccheck(vdev->CreateVideoProcessorOutputView(nv12.Get(), ve.Get(), &ov, &outView), 7);
		ComPtr<IMFMediaBuffer> buffer; ccheck(MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D), nv12.Get(), 0, FALSE, &buffer), 7);
		ComPtr<IMF2DBuffer> b2; ccheck(buffer.As(&b2), 7); DWORD blen = 0; ccheck(b2->GetContiguousLength(&blen), 7); ccheck(buffer->SetCurrentLength(blen), 7);
		ComPtr<IMFSample> sample; ccheck(MFCreateSample(&sample), 7); ccheck(sample->AddBuffer(buffer.Get()), 7);
		HANDLE timer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_MODIFY_STATE | SYNCHRONIZE);
		LARGE_INTEGER freq, now; QueryPerformanceFrequency(&freq); QueryPerformanceCounter(&now);
		const int64_t period = freq.QuadPart / fps, epoch = now.QuadPart; int64_t next = now.QuadPart, lastSubmit = 0;
		ev(2, 1);
		while (!d_quit) {
			QueryPerformanceCounter(&now);
			if (next > now.QuadPart) { LARGE_INTEGER due; due.QuadPart = -((next - now.QuadPart) * 10000000 / freq.QuadPart); SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE); WaitForSingleObject(timer, 1000); QueryPerformanceCounter(&now); }
			next = now.QuadPart - next >= period ? now.QuadPart + period : next + period;
			bool fresh = false;
			ID3D11Texture2D* src = cap.acquire(100, fresh);
			if (!fresh) { QueryPerformanceCounter(&now); if (!src || (!d_idr && now.QuadPart - lastSubmit < freq.QuadPart)) continue; }
			ComPtr<ID3D11VideoProcessorInputView> in; D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC iv{}; iv.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
			ccheck(vdev->CreateVideoProcessorInputView(src, ve.Get(), &iv, &in), 9);
			D3D11_VIDEO_PROCESSOR_STREAM vps{}; vps.Enable = TRUE; vps.pInputSurface = in.Get();
			ccheck(vctx->VideoProcessorBlt(vp.Get(), outView.Get(), 0, 1, &vps), 10);
			cap.release();
			ctx->Flush();
			if (d_idr.exchange(false)) set(CODECAPI_AVEncVideoForceKeyFrame, 1, false);
			QueryPerformanceCounter(&now); lastSubmit = now.QuadPart;
			sample->SetSampleTime((now.QuadPart - epoch) * 10000000 / freq.QuadPart); sample->SetSampleDuration(10000000 / fps);
			ccheck(writer->WriteSample(stream, sample.Get()), 11);
		}
		CloseHandle(timer);
		writer->Finalize();
		MFShutdown();
		ev(3, 0);
	} catch (CaptureError e) { MFShutdown(); ev(4, e.stage * 0x100000 + (int32_t)(e.hr & 0xFFFFF)); }
	CoUninitialize();
}

extern "C" __declspec(dllexport) int32_t vindos_desktop_start(uint32_t fps, uint32_t bitrate, FrameFn onFrame, DesktopEventFn onEvent) {
	std::lock_guard<std::mutex> lock(d_lifecycle);
	if (d_thread.joinable()) return -1;
	d_quit = false; d_idr = true;
	d_thread = std::thread(run, fps, bitrate, onFrame, onEvent);
	return 0;
}
extern "C" __declspec(dllexport) void vindos_desktop_idr() { d_idr = true; }
extern "C" __declspec(dllexport) void vindos_desktop_rect(int32_t* left, int32_t* top, int32_t* right, int32_t* bottom) { *left = d_rect.left; *top = d_rect.top; *right = d_rect.right; *bottom = d_rect.bottom; }
extern "C" __declspec(dllexport) void vindos_desktop_stop() { std::lock_guard<std::mutex> lock(d_lifecycle); d_quit = true; if (d_thread.joinable()) d_thread.join(); }
