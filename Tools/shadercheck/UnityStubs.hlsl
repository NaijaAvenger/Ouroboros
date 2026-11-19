// Stand-ins for the parts of Unity's shader library that this project's shaders use.
// Only for Tools/shadercheck/run.sh, which compiles each shader's code with glslang to catch syntax and
// type errors without Unity. The bodies are placeholders: they need the right types, not the right maths.
#define fixed float
#define fixed2 float2
#define fixed3 float3
#define fixed4 float4
#define half float
#define half2 float2
#define half3 float3
#define half4 float4
#define sampler2D Texture2D
SamplerState ouro_sampler;
#define tex2D(t, uv) t.Sample(ouro_sampler, uv)

float4x4 unity_ObjectToWorld;
float4x4 unity_MatrixVP;
float3 _WorldSpaceCameraPos;
float4 _WorldSpaceLightPos0;
float4 _LightColor0;
float4 _ProjectionParams;
float4 _ScreenParams;
float4 _Time;
#define UNITY_UV_STARTS_AT_TOP 1

struct appdata_base { float4 vertex : POSITION; float3 normal : NORMAL; float4 texcoord : TEXCOORD0; };
struct appdata_full { float4 vertex : POSITION; float4 tangent : TANGENT; float3 normal : NORMAL; float4 texcoord : TEXCOORD0; float4 texcoord1 : TEXCOORD1; float4 color : COLOR; };
struct appdata_img { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
struct v2f_img { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

float4 UnityObjectToClipPos(float4 v) { return mul(unity_MatrixVP, mul(unity_ObjectToWorld, float4(v.xyz, 1.0))); }
float4 UnityObjectToClipPos(float3 v) { return mul(unity_MatrixVP, mul(unity_ObjectToWorld, float4(v, 1.0))); }
float3 UnityObjectToWorldNormal(float3 n) { return normalize(mul((float3x3)unity_ObjectToWorld, n)); }
float3 UnityWorldSpaceLightDir(float3 worldPos) { return _WorldSpaceLightPos0.xyz - worldPos * _WorldSpaceLightPos0.w; }
float3 UnityWorldSpaceViewDir(float3 worldPos) { return _WorldSpaceCameraPos - worldPos; }
float3 ShadeSH9(float4 n) { return n.xyz * 0.5 + 0.5; }
void DecodeDepthNormal(float4 enc, out float depth, out float3 normal) { depth = enc.z; normal = float3(enc.xy, enc.w); }
v2f_img vert_img(appdata_img v) { v2f_img o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

#define TRANSFORM_TEX(tex, name) (tex.xy * name##_ST.xy + name##_ST.zw)
#define UNITY_INITIALIZE_OUTPUT(type, name) name = (type)0;
#define UNITY_LIGHTING_COORDS(a, b) float4 _LightCoord : TEXCOORD##a; float4 _ShadowCoord : TEXCOORD##b;
#define UNITY_TRANSFER_LIGHTING(o, uv) o._LightCoord = float4(uv, 0.0, 0.0); o._ShadowCoord = v.vertex;
#define UNITY_LIGHT_ATTENUATION(dest, input, worldPos) float dest = saturate(input._LightCoord.x + input._ShadowCoord.x + worldPos.x);
#define UNITY_FOG_COORDS(a) float fogCoord : TEXCOORD##a;
#define UNITY_TRANSFER_FOG(o, outpos) o.fogCoord = outpos.z;
#define UNITY_APPLY_FOG(coord, col) col.rgb = lerp(col.rgb, col.rgb, saturate(coord));
#define UNITY_APPLY_FOG_COLOR(coord, col, fogCol) col.rgb = lerp(fogCol.rgb, col.rgb, saturate(coord));
