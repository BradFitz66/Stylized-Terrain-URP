
//https://docs.unity3d.com/Packages/com.unity.shadergraph@6.9/manual/Triplanar-Node.html
float4 triplanar(
	float3 Position,
	float3 Normal,
	sampler2D Texture,
	float Blend,
	float Tile
)
{
	float3 Node_UV = Position * Tile;
	float3 Node_Blend = pow(abs(Normal), Blend);
	Node_Blend /= dot(Node_Blend, 1.0);
	float4 Node_X = tex2D(Texture, Node_UV.yz);
	float4 Node_Y = tex2D(Texture, Node_UV.xz);
	float4 Node_Z = tex2D(Texture, Node_UV.xy);
	return Node_X * Node_Blend.x + Node_Y * Node_Blend.y + Node_Z * Node_Blend.z;
}

float4 TerrainLayer(
	float3 position,
	float3 normal,
	float4 lerp,
	sampler2D texture,
	float control,
	float tiling
)
{
	//float4 color = tex2D(texture, position.xy);
	return float4(0, 0, 0, 0);
}

