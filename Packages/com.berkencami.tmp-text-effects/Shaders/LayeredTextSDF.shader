// Layered SDF text for TextMeshProUGUI, driven by LayeredText + LayeredTextStyle.
//
// The mesh holds one copy of every glyph quad per layer (layer-major, back to front). Per vertex:
//   uv0  = atlas uv (xy), TMP SDF scale (w, negative = bold)
//   uv1  = text-space coords in font-size units (face texture)
//   uv2  = padded glyph rect in the atlas (min.xy, max.xy) — samples outside it read as empty
//   tangent  = horizontal gradient position (x, 0..1), shine sweep position (y), shine on (z), flash (w, face only)
//   uv3  = layer index (-1 = solid vertex-colour quad, e.g. a <mark> highlight), extrude sweep in atlas uv (yz; per character, so arcs keep a fixed screen direction),
//          vertical gradient position (w)
// Per layer (material properties _L{i}A/B/P/V, i < 8):
//   A = color (top / extrude front), B = color2 (bottom / extrude back)
//   P = (kind: 0 fill, 1 face, 2 extrude; dilate; softness; extrude steps). Kind 3 (solid) comes from uv3.x < 0.
//   V = (1 = carries the face extras below; unused; face texture blend; tint with vertex color)
// Face extras (first Face layer): gloss band, inner shadow / highlight (offset baked into uv3.yz).
// _GradientDir rotates every layer's two-colour gradient.

Shader "TMP Text Effects/Layered SDF" {

Properties {
	[HideInInspector] _L0A ("", Color) = (0,0,0,0)
	[HideInInspector] _L0B ("", Color) = (0,0,0,0)
	[HideInInspector] _L0P ("", Vector) = (0,0,0,0)
	[HideInInspector] _L0V ("", Vector) = (0,0,0,0)
	[HideInInspector] _L1A ("", Color) = (0,0,0,0)
	[HideInInspector] _L1B ("", Color) = (0,0,0,0)
	[HideInInspector] _L1P ("", Vector) = (0,0,0,0)
	[HideInInspector] _L1V ("", Vector) = (0,0,0,0)
	[HideInInspector] _L2A ("", Color) = (0,0,0,0)
	[HideInInspector] _L2B ("", Color) = (0,0,0,0)
	[HideInInspector] _L2P ("", Vector) = (0,0,0,0)
	[HideInInspector] _L2V ("", Vector) = (0,0,0,0)
	[HideInInspector] _L3A ("", Color) = (0,0,0,0)
	[HideInInspector] _L3B ("", Color) = (0,0,0,0)
	[HideInInspector] _L3P ("", Vector) = (0,0,0,0)
	[HideInInspector] _L3V ("", Vector) = (0,0,0,0)
	[HideInInspector] _L4A ("", Color) = (0,0,0,0)
	[HideInInspector] _L4B ("", Color) = (0,0,0,0)
	[HideInInspector] _L4P ("", Vector) = (0,0,0,0)
	[HideInInspector] _L4V ("", Vector) = (0,0,0,0)
	[HideInInspector] _L5A ("", Color) = (0,0,0,0)
	[HideInInspector] _L5B ("", Color) = (0,0,0,0)
	[HideInInspector] _L5P ("", Vector) = (0,0,0,0)
	[HideInInspector] _L5V ("", Vector) = (0,0,0,0)
	[HideInInspector] _L6A ("", Color) = (0,0,0,0)
	[HideInInspector] _L6B ("", Color) = (0,0,0,0)
	[HideInInspector] _L6P ("", Vector) = (0,0,0,0)
	[HideInInspector] _L6V ("", Vector) = (0,0,0,0)
	[HideInInspector] _L7A ("", Color) = (0,0,0,0)
	[HideInInspector] _L7B ("", Color) = (0,0,0,0)
	[HideInInspector] _L7P ("", Vector) = (0,0,0,0)
	[HideInInspector] _L7V ("", Vector) = (0,0,0,0)
	[HideInInspector] _LayerCount ("Layer Count", Float) = 0

	_FaceTex			("Face Texture", 2D) = "white" {}
	[HideInInspector] _GradientDir ("", Vector) = (0,1,0,0)
	[HideInInspector] _GlossColor ("", Color) = (1,1,1,0)
	[HideInInspector] _GlossParams ("", Vector) = (0.55,0.04,0.25,0.2)
	[HideInInspector] _InnerShadowColor ("", Color) = (0,0,0,0)
	[HideInInspector] _InnerHighlightColor ("", Color) = (1,1,1,0)
	[HideInInspector] _InnerParams ("", Vector) = (0.3,0,0,0)
	[HideInInspector] _ShineColor ("", Color) = (1,1,1,0)
	[HideInInspector] _ShineParams ("", Vector) = (1,0,0.18,0.06)

	// TMP-compatible SDF properties (TMP reads/writes these on the font material).
	_FaceColor			("Face Color", Color) = (1,1,1,1)
	_FaceDilate			("Face Dilate", Range(-1,1)) = 0
	_OutlineWidth		("Outline Thickness", Range(0,1)) = 0	// unused; read by TMP's padding/ratio code
	_OutlineSoftness	("Outline Softness", Range(0,1)) = 0	// unused; read by TMP's padding/ratio code
	_WeightNormal		("Weight Normal", float) = 0
	_WeightBold			("Weight Bold", float) = .5
	_ShaderFlags		("Flags", float) = 0
	_ScaleRatioA		("Scale RatioA", float) = 1
	_ScaleRatioB		("Scale RatioB", float) = 1
	_ScaleRatioC		("Scale RatioC", float) = 1
	_MainTex			("Font Atlas", 2D) = "white" {}
	_TextureWidth		("Texture Width", float) = 512
	_TextureHeight		("Texture Height", float) = 512
	_GradientScale		("Gradient Scale", float) = 5
	_ScaleX				("Scale X", float) = 1
	_ScaleY				("Scale Y", float) = 1
	_PerspectiveFilter	("Perspective Correction", Range(0, 1)) = 0.875
	_Sharpness			("Sharpness", Range(-1,1)) = 0
	_VertexOffsetX		("Vertex OffsetX", float) = 0
	_VertexOffsetY		("Vertex OffsetY", float) = 0

	_ClipRect			("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
	_MaskSoftnessX		("Mask SoftnessX", float) = 0
	_MaskSoftnessY		("Mask SoftnessY", float) = 0

	_StencilComp		("Stencil Comparison", Float) = 8
	_Stencil			("Stencil ID", Float) = 0
	_StencilOp			("Stencil Operation", Float) = 0
	_StencilWriteMask	("Stencil Write Mask", Float) = 255
	_StencilReadMask	("Stencil Read Mask", Float) = 255

	_CullMode			("Cull Mode", Float) = 0
	_ColorMask			("Color Mask", Float) = 15
}

SubShader {
	Tags
	{
		"Queue"="Transparent"
		"IgnoreProjector"="True"
		"RenderType"="Transparent"
	}

	Stencil
	{
		Ref [_Stencil]
		Comp [_StencilComp]
		Pass [_StencilOp]
		ReadMask [_StencilReadMask]
		WriteMask [_StencilWriteMask]
	}

	Cull [_CullMode]
	ZWrite Off
	Lighting Off
	Fog { Mode Off }
	ZTest [unity_GUIZTestMode]
	Blend One OneMinusSrcAlpha
	ColorMask [_ColorMask]

	Pass {
		CGPROGRAM
		#pragma target 3.0
		#pragma vertex VertShader
		#pragma fragment PixShader

		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP

		#include "UnityCG.cginc"
		#include "UnityUI.cginc"

		#define MAX_EXTRUDE_STEPS 32

		sampler2D _MainTex;
		sampler2D _FaceTex;
		float4 _FaceTex_ST;
		float4 _GradientDir;
		float4 _GlossColor;
		float4 _GlossParams;			// position, softness, curve, inset
		float4 _InnerShadowColor;
		float4 _InnerHighlightColor;
		float4 _InnerParams;			// softness
		float4 _ShineColor;
		float4 _ShineParams;			// direction (xy), width, softness

		float _GradientScale;
		float _WeightNormal;
		float _WeightBold;
		float _ScaleX;
		float _ScaleY;
		float _PerspectiveFilter;
		float _Sharpness;
		float _VertexOffsetX;
		float _VertexOffsetY;

		float4 _ClipRect;
		float _MaskSoftnessX;
		float _MaskSoftnessY;
		float _UIMaskSoftnessX;
		float _UIMaskSoftnessY;
		int _UIVertexColorAlwaysGammaSpace;

		float4 _L0A, _L0B, _L0P, _L0V;
		float4 _L1A, _L1B, _L1P, _L1V;
		float4 _L2A, _L2B, _L2P, _L2V;
		float4 _L3A, _L3B, _L3P, _L3V;
		float4 _L4A, _L4B, _L4P, _L4V;
		float4 _L5A, _L5B, _L5P, _L5V;
		float4 _L6A, _L6B, _L6P, _L6V;
		float4 _L7A, _L7B, _L7P, _L7V;

		struct vertex_t {
			UNITY_VERTEX_INPUT_INSTANCE_ID
			float4	vertex		: POSITION;
			float3	normal		: NORMAL;
			float4	tangent		: TANGENT;
			fixed4	color		: COLOR;
			float4	texcoord0	: TEXCOORD0;
			float2	texcoord1	: TEXCOORD1;
			float4	texcoord2	: TEXCOORD2;
			float4	texcoord3	: TEXCOORD3;
		};

		struct pixel_t {
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
			float4	vertex		: SV_POSITION;
			half4	colorA		: COLOR;		// fill/face color, or extrude front (straight alpha)
			half4	colorB		: COLOR1;		// extrude back (straight alpha)
			float4	uv			: TEXCOORD0;	// atlas uv (xy), face texture uv (zw)
			half4	param		: TEXCOORD1;	// sdf scale, bias, kind, extrude steps
			half4	mask		: TEXCOORD2;	// clip-rect position (xy), softness (zw)
			float4	rect		: TEXCOORD3;	// padded glyph rect in atlas uv
			float4	extra		: TEXCOORD4;	// extrude sweep / inner offset in uv (xy), face texture blend (z)
			half4	face		: TEXCOORD5;	// inner sdf scale, inner bias, gloss bias, 1 = face extras on
			half	flash		: TEXCOORD7;	// face blend towards white
			float4	grad		: TEXCOORD6;	// gradient position x, y; shine position; shine on
		};

		void GetLayer(int i, out float4 a, out float4 b, out float4 p, out float4 v)
		{
			a = _L0A; b = _L0B; p = _L0P; v = _L0V;
			if (i == 1) { a = _L1A; b = _L1B; p = _L1P; v = _L1V; }
			else if (i == 2) { a = _L2A; b = _L2B; p = _L2P; v = _L2V; }
			else if (i == 3) { a = _L3A; b = _L3B; p = _L3P; v = _L3V; }
			else if (i == 4) { a = _L4A; b = _L4B; p = _L4P; v = _L4V; }
			else if (i == 5) { a = _L5A; b = _L5B; p = _L5P; v = _L5V; }
			else if (i == 6) { a = _L6A; b = _L6B; p = _L6P; v = _L6V; }
			else if (i == 7) { a = _L7A; b = _L7B; p = _L7P; v = _L7V; }
		}

		pixel_t VertShader(vertex_t input)
		{
			pixel_t output;
			UNITY_INITIALIZE_OUTPUT(pixel_t, output);
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_TRANSFER_INSTANCE_ID(input, output);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

			float4 la, lb, lp, lv;
			bool solid = input.texcoord3.x < -0.5;
			GetLayer((int)(max(0, input.texcoord3.x) + 0.5), la, lb, lp, lv);
			float kind = solid ? 3 : lp.x;

			float4 vert = input.vertex;
			vert.x += _VertexOffsetX;
			vert.y += _VertexOffsetY;
			float4 vPosition = UnityObjectToClipPos(vert);

			// SDF screen-space scale — same derivation as TMP's shaders.
			float2 pixelSize = vPosition.w;
			pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
			float scale = rsqrt(dot(pixelSize, pixelSize));
			scale *= abs(input.texcoord0.w) * _GradientScale * (_Sharpness + 1);
			if (UNITY_MATRIX_P[3][3] == 0) scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale, abs(dot(UnityObjectToWorldNormal(input.normal.xyz), normalize(WorldSpaceViewDir(vert)))));

			float bold = step(input.texcoord0.w, 0);
			float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0 * 0.5;

			float softness = lp.z;
			float dilate = lp.y;
			float s = scale / (1 + softness * scale);
			// Never let the SDF's zero level (the padding border) count as covered: with a big dilate/softness
			// the whole padded quad would fill. Clamping reaches the padding edge and fades out exactly there.
			float bias = max(0, (0.5 - weight) * s - 0.5 - dilate * 0.5 * s);

			if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
				input.color.rgb = UIGammaToLinear(input.color.rgb);

			float gy = saturate(input.texcoord3.w);
			float gx = saturate(input.tangent.x);
			// Two-colour gradient along _GradientDir (0,1 = vertical) through the centre of the gradient box.
			float gt = saturate(dot(float2(gx, gy) - 0.5, _GradientDir.xy) + 0.5);
			half4 colA, colB;
			if (kind > 2.5)
			{
				// Solid: TMP's own colour, no SDF (highlight quads carry no SDF scale).
				colA = input.color;
				colB = colA;
			}
			else if (kind > 1.5)
			{
				// Extrude: front -> back along the sweep.
				colA = la;
				colB = lb;
			}
			else
			{
				// Fill / face: gradient, bottom (B) -> top (A).
				colA = lerp(lb, la, gt);
				colB = colA;
				if (kind > 0.5 && lv.w > 0.5) colA.rgb *= input.color.rgb;
			}
			if (!solid)
			{
				colA.a *= input.color.a;
				colB.a *= input.color.a;
			}

			float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
			const half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));

			output.vertex = vPosition;
			output.colorA = colA;
			output.colorB = colB;
			output.uv = float4(input.texcoord0.xy, TRANSFORM_TEX(input.texcoord1, _FaceTex));
			output.param = half4(s, bias, kind, lp.w);
			output.mask = half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + pixelSize.xy));
			output.rect = input.texcoord2;
			output.extra = float4(input.texcoord3.yz, kind > 0.5 && kind < 1.5 ? lv.z : 0, 0);

			bool faceExtras = kind > 0.5 && kind < 1.5 && lv.x > 0.5;
			float sInner = scale / (1 + _InnerParams.x * scale);
			float biasInner = max(0, (0.5 - weight) * sInner - 0.5 - dilate * 0.5 * sInner);
			float biasGloss = max(0, (0.5 - weight) * s - 0.5 - (dilate - _GlossParams.w) * 0.5 * s);
			output.face = half4(sInner, biasInner, biasGloss, faceExtras ? 1 : 0);
			output.grad = float4(gx, gy, input.tangent.y, input.tangent.z);
			output.flash = kind > 0.5 && kind < 1.5 ? input.tangent.w : 0;
			return output;
		}

		// Layer coverage at uv. Outside the glyph's padded atlas rect there is nothing: never a neighbour
		// glyph, and no haze from large softness/dilate pushing the SDF's zero level above zero.
		half Coverage(float2 uv, float4 rect, half s, half bias)
		{
			float2 inside = step(rect.xy, uv) * step(uv, rect.zw);
			half d = tex2Dlod(_MainTex, float4(uv, 0, 0)).a;
			return saturate(d * s - bias) * inside.x * inside.y;
		}

		fixed4 PixShader(pixel_t input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);

			half s = input.param.x;
			half bias = input.param.y;
			half kind = input.param.z;
			half4 faceTex = tex2D(_FaceTex, input.uv.zw);

			half4 c = 0;
			if (kind > 2.5)
			{
				c = input.colorA;
				c.rgb *= c.a;
			}
			else if (kind > 1.5)
			{
				// Composite the sweep back to front, like stacking copies: the frontmost covering copy wins.
				int steps = clamp((int)(input.param.w + 0.5), 1, MAX_EXTRUDE_STEPS);
				float invSteps = 1.0 / steps;
				[loop]
				for (int k = steps; k >= 0; k--)
				{
					float t = k * invSteps;
					half a = Coverage(input.uv.xy - t * input.extra.xy, input.rect, s, bias);
					half4 col = lerp(input.colorA, input.colorB, t);
					col.rgb *= col.a;
					c = col * a + c * (1 - a);
				}
			}
			else
			{
				half a = Coverage(input.uv.xy, input.rect, s, bias);
				half4 col = input.colorA;
				col.rgb *= lerp(half3(1, 1, 1), faceTex.rgb, input.extra.z);
				col.a *= lerp(1, faceTex.a, input.extra.z);

				if (input.face.w > 0.5)
				{
					// Inner shadow / highlight: where the face shifted along the light direction leaves the glyph.
					half shifted = Coverage(input.uv.xy + input.extra.xy, input.rect, input.face.x, input.face.y);
					half unshifted = Coverage(input.uv.xy - input.extra.xy, input.rect, input.face.x, input.face.y);
					col.rgb = lerp(col.rgb, _InnerShadowColor.rgb, (1 - shifted) * _InnerShadowColor.a);
					col.rgb = lerp(col.rgb, _InnerHighlightColor.rgb, (1 - unshifted) * _InnerHighlightColor.a);

					// Gloss: band above a (curved) edge, kept away from the glyph outline by the inset.
					float cx = input.grad.x - 0.5;
					float edge = _GlossParams.x - _GlossParams.z * 4 * cx * cx;
					half band = smoothstep(edge - _GlossParams.y, edge + _GlossParams.y + 1e-4, input.grad.y);
					half inset = Coverage(input.uv.xy, input.rect, s, input.face.z);
					col.rgb = lerp(col.rgb, _GlossColor.rgb, band * inset * _GlossColor.a);

					// Shine: a band across the gradient box at the animator-driven sweep position.
					if (input.grad.w > 0.5)
					{
						float along = dot(input.grad.xy - 0.5, _ShineParams.xy) + 0.5;
						half halfWidth = _ShineParams.z * 0.5;
						half shine = 1 - smoothstep(halfWidth - _ShineParams.w, halfWidth + _ShineParams.w + 1e-4, abs(along - input.grad.z));
						col.rgb = lerp(col.rgb, _ShineColor.rgb, shine * _ShineColor.a);
					}
				}
				col.rgb = lerp(col.rgb, 1, input.flash);
				col.rgb *= col.a;
				c = col * a;
			}

			#if UNITY_UI_CLIP_RECT
			half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
			c *= m.x * m.y;
			#endif

			#if UNITY_UI_ALPHACLIP
			clip(c.a - 0.001);
			#endif

			return c;
		}
		ENDCG
	}
}
}
