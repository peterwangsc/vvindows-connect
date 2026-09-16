#pragma once
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <cstring>
#include <stdexcept>
#include <vector>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")

struct CaptureError { int stage; HRESULT hr; };
inline void ccheck(HRESULT hr, int stage) { if (FAILED(hr)) throw CaptureError{ stage, hr }; }

struct Capture {
	Microsoft::WRL::ComPtr<ID3D11Device> dev; Microsoft::WRL::ComPtr<ID3D11DeviceContext> ctx;
	Microsoft::WRL::ComPtr<IDXGIOutputDuplication> dup;
	Microsoft::WRL::ComPtr<ID3D11VertexShader> vs; Microsoft::WRL::ComPtr<ID3D11PixelShader> ps;
	Microsoft::WRL::ComPtr<ID3D11Texture2D> background, composed, last, pointer;
	Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> backgroundView, pointerView;
	Microsoft::WRL::ComPtr<ID3D11RenderTargetView> target;
	Microsoft::WRL::ComPtr<ID3D11Buffer> constants;
	DXGI_OUTDUPL_POINTER_SHAPE_INFO shape{}; DXGI_OUTDUPL_POINTER_POSITION position{}; std::vector<BYTE> shapeBytes;
	RECT rect{}; UINT w = 0, h = 0; bool haveLast = false, held = false;

	static constexpr const char* shader = R"(
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

	void init(ID3D11Device* device, ID3D11DeviceContext* context) {
		dev = device; ctx = context;
		Microsoft::WRL::ComPtr<IDXGIDevice> dxgi; ccheck(dev.As(&dxgi), 3); Microsoft::WRL::ComPtr<IDXGIAdapter> adapter; ccheck(dxgi->GetAdapter(&adapter), 3);
		Microsoft::WRL::ComPtr<IDXGIOutput> output; ccheck(adapter->EnumOutputs(0, &output), 3); Microsoft::WRL::ComPtr<IDXGIOutput1> output1; ccheck(output.As(&output1), 3);
		DXGI_OUTPUT_DESC od{}; output->GetDesc(&od); rect = od.DesktopCoordinates;
		ccheck(output1->DuplicateOutput(dev.Get(), &dup), 3);
		DXGI_OUTDUPL_DESC dd; dup->GetDesc(&dd); w = dd.ModeDesc.Width & ~1u; h = dd.ModeDesc.Height & ~1u;
		Microsoft::WRL::ComPtr<ID3DBlob> blob, errors;
		ccheck(D3DCompile(shader, strlen(shader), nullptr, nullptr, nullptr, "vs", "vs_5_0", 0, 0, &blob, &errors), 7);
		ccheck(dev->CreateVertexShader(blob->GetBufferPointer(), blob->GetBufferSize(), nullptr, &vs), 7); blob.Reset();
		ccheck(D3DCompile(shader, strlen(shader), nullptr, nullptr, nullptr, "ps", "ps_5_0", 0, 0, &blob, &errors), 7);
		ccheck(dev->CreatePixelShader(blob->GetBufferPointer(), blob->GetBufferSize(), nullptr, &ps), 7);
		D3D11_TEXTURE2D_DESC td{}; td.Width = w; td.Height = h; td.MipLevels = td.ArraySize = 1; td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1; td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
		ccheck(dev->CreateTexture2D(&td, nullptr, &background), 7); ccheck(dev->CreateShaderResourceView(background.Get(), nullptr, &backgroundView), 7);
		td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE; ccheck(dev->CreateTexture2D(&td, nullptr, &composed), 7); ccheck(dev->CreateRenderTargetView(composed.Get(), nullptr, &target), 7);
		td.BindFlags = 0; ccheck(dev->CreateTexture2D(&td, nullptr, &last), 7);
		D3D11_BUFFER_DESC bd{}; bd.ByteWidth = 32; bd.Usage = D3D11_USAGE_DEFAULT; bd.BindFlags = D3D11_BIND_CONSTANT_BUFFER; ccheck(dev->CreateBuffer(&bd, nullptr, &constants), 7);
	}

	ID3D11Texture2D* acquire(UINT timeoutMs, bool& fresh) {
		DXGI_OUTDUPL_FRAME_INFO info{}; Microsoft::WRL::ComPtr<IDXGIResource> resource;
		HRESULT hr = dup->AcquireNextFrame(timeoutMs, &info, &resource);
		fresh = false;
		if (hr == DXGI_ERROR_WAIT_TIMEOUT) return haveLast ? last.Get() : nullptr;
		if (hr == DXGI_ERROR_ACCESS_LOST) throw CaptureError{ 8, hr };
		ccheck(hr, 8);
		held = true;
		Microsoft::WRL::ComPtr<ID3D11Texture2D> tex; ccheck(resource.As(&tex), 8);
		if (info.LastMouseUpdateTime.QuadPart) position = info.PointerPosition;
		if (info.PointerShapeBufferSize) uploadShape(info.PointerShapeBufferSize);
		ID3D11Texture2D* src = tex.Get();
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
		ctx->CopyResource(last.Get(), src); haveLast = true; fresh = true;
		return last.Get();
	}

	void release() { if (held) { dup->ReleaseFrame(); held = false; } }

	void uploadShape(UINT size) {
		shapeBytes.resize(size); UINT need = 0;
		if (FAILED(dup->GetFramePointerShape((UINT)shapeBytes.size(), shapeBytes.data(), &need, &shape))) return;
		const bool mono = shape.Type == DXGI_OUTDUPL_POINTER_SHAPE_TYPE_MONOCHROME; const UINT sh = mono ? shape.Height / 2 : shape.Height;
		if (!shape.Width || !sh || shape.Width > 4096 || sh > 4096) return;
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
};
