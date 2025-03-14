﻿using MemoryMappedFileIPC;
using ResoniteBridgeLib;
using ResoniteUnityExporterShared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using static ResoniteUnityExporter.ResoniteTransferUtils;

namespace ResoniteUnityExporter
{
    public class ResoniteTransferMesh
    {
        public static List<UnityEngine.Rendering.VertexAttribute> AllUVAttributes =
            new List<UnityEngine.Rendering.VertexAttribute>()
            {
                UnityEngine.Rendering.VertexAttribute.TexCoord0,
                UnityEngine.Rendering.VertexAttribute.TexCoord1,
                UnityEngine.Rendering.VertexAttribute.TexCoord2,
                UnityEngine.Rendering.VertexAttribute.TexCoord3,
                UnityEngine.Rendering.VertexAttribute.TexCoord4,
                UnityEngine.Rendering.VertexAttribute.TexCoord5,
                UnityEngine.Rendering.VertexAttribute.TexCoord6,
                UnityEngine.Rendering.VertexAttribute.TexCoord7
            };

        public static int[] GetMeshUVChannelDimensions(UnityEngine.Mesh unityMesh, out int[] actualTexCoordIndices, out int maxUVIndex)
        {
            // Coroutines is a property

            List<int> texCoordIndices = new List<int>();
            List<int> texCoordDimensions = new List<int>();
            maxUVIndex = 0;
            foreach (UnityEngine.Rendering.VertexAttributeDescriptor descriptor in unityMesh.GetVertexAttributes())
            {
                int uvIndex = AllUVAttributes.IndexOf(descriptor.attribute);
                if (uvIndex != -1)
                {
                    texCoordIndices.Add(uvIndex);
                    texCoordDimensions.Add(descriptor.dimension);
                    maxUVIndex = Math.Max(maxUVIndex, uvIndex);
                }
            }
            actualTexCoordIndices = texCoordIndices.ToArray();
            return texCoordDimensions.ToArray();
        }

        public static void ToByteArray(object source, int sourceOffsetInBytes, byte[] destination, int offsetInDestinationInBytes, int lenOfSourceInBytes)
        {
            if (lenOfSourceInBytes <= 0)
            {
                return;
            }

            GCHandle gCHandle = GCHandle.Alloc(source, GCHandleType.Pinned);
            try
            {
                Marshal.Copy(gCHandle.AddrOfPinnedObject() + sourceOffsetInBytes, destination, offsetInDestinationInBytes, lenOfSourceInBytes);
            }
            finally
            {
                if (gCHandle.IsAllocated)
                {
                    gCHandle.Free();
                }
            }
        }

        public static int[] GetSubMeshesVertexSubset(UnityEngine.Mesh mesh, int subMeshStartIndex, int subMeshEndExclusive, out int[] indexMap, out bool[] whichVerticesPresent)
        {
            if (mesh.vertices.Length == 0)
            {
                indexMap = new int[0];
                whichVerticesPresent = new bool[0];
                return new int[0];
            }
            // start as false
            whichVerticesPresent = new bool[mesh.vertices.Length];
            for (int subMeshI = subMeshStartIndex; subMeshI < subMeshEndExclusive; subMeshI++)
            {
                int[] indices = mesh.GetIndices(subMeshI);
                for (int i = 0; i < indices.Length; i++)
                {
                    whichVerticesPresent[indices[i]] = true;
                }
            }
            List<int> presentVertices = new List<int>();
            indexMap = new int[mesh.vertices.Length];
            for (int i = 0; i < whichVerticesPresent.Length; i++)
            {
                if (whichVerticesPresent[i])
                {
                    indexMap[i] = presentVertices.Count;
                    presentVertices.Add(i);
                }
            }
            return presentVertices.ToArray();
        }

        public static OutType[] ConvertSubset<OutType, InType>(InType[] data, int[] subset) where OutType : struct where InType : struct
        {
            InType[] subsetData = new InType[subset.Length];
            for (int i = 0; i < subset.Length; i++)
            {
                subsetData[i] = data[subset[i]];
            }
            return SerializationUtils.ConvertArray<OutType, InType>(subsetData);
        }

        public static ResoniteUnityExporterShared.StaticMesh_U2Res ConvertMesh(UnityEngine.Mesh unityMesh, string[] boneNames, Matrix4x4 submeshTransform, int subMeshStartIndex, int subMeshEndIndexExclusive, float scaleFactor)
        {
            // todo: provide option to ignore bones and ignore vertex colors
            StaticMesh_U2Res meshx = new StaticMesh_U2Res();
            meshx.name = unityMesh.name;
            int maxUVIndex;
            int[] uvDimensions = GetMeshUVChannelDimensions(unityMesh, out int[] actualTexCoordIndices, out maxUVIndex);

            Float3_U2Res[] vertices = null;
            Float3_U2Res[] normals = null;
            Float4_U2Res[] tangents = null;
            int numVertices;
            //string path = AssetDatabase.GetAssetPath(unityMesh);
            //ModelImporter modelImporter = AssetImporter.GetAtPath(path) as ModelImporter;
            // unapply global scale since we will reapply global scaling on import

            bool convertSubmesh = subMeshStartIndex != -1 &&
                subMeshEndIndexExclusive != -1 &&
                unityMesh.subMeshCount >= subMeshEndIndexExclusive;

            int[] submeshesVertexSubset = null;
            int[] submeshesIndexMap = null;
            bool[] whichVerticesPresent = null;
            if (convertSubmesh)
            {
                submeshesVertexSubset = GetSubMeshesVertexSubset(unityMesh, subMeshStartIndex, subMeshEndIndexExclusive, out submeshesIndexMap, out whichVerticesPresent);
            }

            using (Timer _ = new Timer("Mesh data"))
            {
                if (convertSubmesh)
                {
                    UnityEngine.Vector3[] unityVerts = ConvertSubset<UnityEngine.Vector3, UnityEngine.Vector3>(unityMesh.vertices, submeshesVertexSubset);
                    for (int i = 0; i < unityVerts.Length; i++)
                    {
                        unityVerts[i] = submeshTransform.MultiplyPoint(unityVerts[i]);
                    }
                    vertices = SerializationUtils.ConvertArray<Float3_U2Res, Vector3>(unityVerts);
                }
                else
                {
                    vertices = SerializationUtils.ConvertArray<Float3_U2Res, UnityEngine.Vector3>(unityMesh.vertices);
                }
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i].x *= scaleFactor;
                    vertices[i].y *= scaleFactor;
                    vertices[i].z *= scaleFactor;
                }
                numVertices = vertices.Length;
                meshx.positions = vertices;
                if (NotEmpty(unityMesh.colors))
                {
                    // important to use .colors instead of .colors32 on the unityMesh
                    // because colors32 stores each r,g,b,a as a byte instead of a float
                    // so this conversion would not work without manually fixing
                    if (convertSubmesh)
                    {
                        meshx.colors = ConvertSubset<Float4_U2Res, UnityEngine.Color>(unityMesh.colors, submeshesVertexSubset);
                    }
                    else
                    {
                        meshx.colors = SerializationUtils.ConvertArray<Float4_U2Res, UnityEngine.Color>(unityMesh.colors);
                    }
                }

                if (NotEmpty(unityMesh.normals))
                {
                    if (convertSubmesh)
                    {
                        Vector3[] unityNormals = ConvertSubset<Vector3, UnityEngine.Vector3>(unityMesh.normals, submeshesVertexSubset);
                        for (int i = 0; i < unityNormals.Length; i++)
                        {
                            unityNormals[i] = submeshTransform.MultiplyVector(unityNormals[i]);
                        }
                        normals = SerializationUtils.ConvertArray<Float3_U2Res, Vector3>(unityNormals);
                    }
                    else
                    {
                        normals = SerializationUtils.ConvertArray<Float3_U2Res, UnityEngine.Vector3>(unityMesh.normals);
                    }
                    meshx.normals = normals;
                }
                if (NotEmpty(unityMesh.tangents))
                {
                    if (convertSubmesh)
                    {
                        Vector4[] unityTangents = ConvertSubset<Vector4, UnityEngine.Vector4>(unityMesh.tangents, submeshesVertexSubset);
                        for (int i = 0; i < unityTangents.Length; i++)
                        {
                            // first three are the vector which is what matters
                            Vector3 result = submeshTransform.MultiplyVector(
                                new Vector3(unityTangents[i].x,
                                unityTangents[i].y,
                                unityTangents[i].z)).normalized;
                            unityTangents[i].x = result.x;
                            unityTangents[i].y = result.y;
                            unityTangents[i].z = result.z;
                        }
                        tangents = SerializationUtils.ConvertArray<Float4_U2Res, Vector4>(unityTangents);
                    }
                    else
                    {
                        tangents = SerializationUtils.ConvertArray<Float4_U2Res, UnityEngine.Vector4>(unityMesh.tangents);
                    }
                    meshx.tangents = tangents;
                }
            }

            // uvs are stored as UV_Array[] uv_channels
            // where uv_channels[0] is for UV0 array, uv_channels[1] is for UV1 array, etc.
            // inside a UV_Array they have three arrays,
            // float2[] uv_2D
            // float3[] uv_3D
            // float4[] uv_4D
            // so we can just set that directly
            using (Timer _ = new Timer("UV data"))
            {
                UVArray_U2Res[] allUvs = new UVArray_U2Res[maxUVIndex+1];
                for (int i = 0; i < actualTexCoordIndices.Length; i++)
                {
                    int uvIndex = actualTexCoordIndices[i];
                    UVArray_U2Res uvArrayI = new UVArray_U2Res();
                    int curDimension = uvDimensions[i];
                    if (curDimension == 2)
                    {
                        List<UnityEngine.Vector2> uvs = new List<UnityEngine.Vector2>(unityMesh.vertexCount);
                        unityMesh.GetUVs(uvIndex, uvs);
                        if (convertSubmesh)
                        {
                            uvArrayI.uv_2D = ConvertSubset<Float2_U2Res, UnityEngine.Vector2>(uvs.ToArray(), submeshesVertexSubset);
                        }
                        else
                        {
                            uvArrayI.uv_2D = SerializationUtils.ConvertArray<Float2_U2Res, UnityEngine.Vector2>(uvs.ToArray());
                        }
                    }
                    else if (curDimension == 3)
                    {
                        List<UnityEngine.Vector3> uvs = new List<UnityEngine.Vector3>(unityMesh.vertexCount);
                        unityMesh.GetUVs(uvIndex, uvs);
                        if (convertSubmesh)
                        {
                            uvArrayI.uv_3D = ConvertSubset<Float3_U2Res, UnityEngine.Vector3>(uvs.ToArray(), submeshesVertexSubset);
                        }
                        else
                        {
                            uvArrayI.uv_3D = SerializationUtils.ConvertArray<Float3_U2Res, UnityEngine.Vector3>(uvs.ToArray());
                        }
                    }
                    else if (curDimension == 4)
                    {
                        List<UnityEngine.Vector4> uvs = new List<UnityEngine.Vector4>(unityMesh.vertexCount);
                        unityMesh.GetUVs(uvIndex, uvs);
                        if (convertSubmesh)
                        {
                            uvArrayI.uv_4D = ConvertSubset<Float4_U2Res, UnityEngine.Vector4>(uvs.ToArray(), submeshesVertexSubset);
                        }
                        else
                        {
                            uvArrayI.uv_4D = SerializationUtils.ConvertArray<Float4_U2Res, UnityEngine.Vector4>(uvs.ToArray());
                        }
                    }
                    uvArrayI.dimension = curDimension;
                    allUvs[uvIndex] = uvArrayI;
                }
                meshx.uvChannels = allUvs;
            }

            using (Timer _ = new Timer("Submesh data"))
            {
                int subMeshStart = subMeshStartIndex >= 0 
                    ? subMeshStartIndex
                    : 0;
                int subMeshEndExclusive = subMeshEndIndexExclusive >= 0
                    ? subMeshEndIndexExclusive
                    : unityMesh.subMeshCount;
                // submesh (index buffers)
                TriSubmesh_U2Res[] submeshes = new TriSubmesh_U2Res[subMeshEndExclusive-subMeshStart];
                int indexInSubmeshesArray = 0;
                for (int subMeshI = subMeshStart; subMeshI < subMeshEndExclusive; subMeshI++)
                {
                    UnityEngine.Rendering.SubMeshDescriptor subMeshDescriptor = unityMesh.GetSubMesh(subMeshI);
                    TriSubmesh_U2Res submesh = new TriSubmesh_U2Res();
                    // todo: Lines, LineStrip, Points
                    int[] indicies = unityMesh.GetIndices(subMeshI);
                    int numPrimitives = 0;
                    bool reverse = false;
                    if (subMeshDescriptor.topology == UnityEngine.MeshTopology.Triangles)
                    {
                        numPrimitives = indicies.Length / 3;
                        if (reverse)
                        {
                            // do it in place because why not
                            for (int triI = 0; triI < indicies.Length; triI++)
                            {
                                int v0 = indicies[triI];
                                int v1 = indicies[triI + 1];
                                int v2 = indicies[triI + 2];
                                indicies[triI] = v2;
                                indicies[triI + 1] = v1;
                                indicies[triI + 2] = v0;
                            }
                        }
                    }
                    else if (subMeshDescriptor.topology == UnityEngine.MeshTopology.Quads)
                    {
                        // turn each quad into two tris
                        numPrimitives = 2 * (indicies.Length / 4);
                        int[] triIndicies = new int[numPrimitives * 3];
                        int triIndex = 0;
                        for (int quadI = 0; quadI < indicies.Length; quadI += 4)
                        {
                            int v0 = indicies[quadI];
                            int v1 = indicies[quadI + 1];
                            int v2 = indicies[quadI + 2];
                            int v3 = indicies[quadI + 3];
                            if (reverse)
                            {
                                triIndicies[triIndex++] = v3;
                                triIndicies[triIndex++] = v2;
                                triIndicies[triIndex++] = v1;

                                triIndicies[triIndex++] = v3;
                                triIndicies[triIndex++] = v1;
                                triIndicies[triIndex++] = v0;
                            }
                            else
                            {
                                triIndicies[triIndex++] = v0;
                                triIndicies[triIndex++] = v1;
                                triIndicies[triIndex++] = v2;

                                triIndicies[triIndex++] = v0;
                                triIndicies[triIndex++] = v2;
                                triIndicies[triIndex++] = v3;
                            }
                        }
                        indicies = triIndicies;
                    }
                    if (convertSubmesh)
                    {
                        // need to renamp if we are only using subsets of indicies
                        int[] correctedIndicies = new int[indicies.Length];
                        for (int i = 0; i < indicies.Length; i++)
                        {
                            correctedIndicies[i] = submeshesIndexMap[indicies[i]];
                        }
                        indicies = correctedIndicies;
                    }
                    submesh.indicies = indicies;
                    submeshes[indexInSubmeshesArray++] = submesh;
                }
                meshx.submeshes = submeshes; // hhf
            }

            using (Timer _ = new Timer("Blendshape data"))
            {
                // these only exist for skinned mesh so we don't need to worry about submeshTransform stuff
                BlendShape_U2Res[] blendShapes = new BlendShape_U2Res[unityMesh.blendShapeCount];

                for (int blendShapeI = 0; blendShapeI < unityMesh.blendShapeCount; blendShapeI++)
                {
                    BlendShape_U2Res blendShape = new BlendShape_U2Res();
                    blendShape.name = unityMesh.GetBlendShapeName(blendShapeI);
                    int blendShapeFrameCount = unityMesh.GetBlendShapeFrameCount(blendShapeI);
                    blendShape.frames = new BlendShapeFrame_U2Res[blendShapeFrameCount];
                    for (int blendShapeFrameI = 0; blendShapeFrameI < blendShapeFrameCount; blendShapeFrameI++)
                    {
                        BlendShapeFrame_U2Res frame = new BlendShapeFrame_U2Res();
                        // todo: ModelImporter just uses 1.0 for weight, should we do that? Answer: yes we should, doesn't work otherwise
                        frame.frameWeight = unityMesh.GetBlendShapeFrameWeight(blendShapeI, blendShapeFrameI);
                        frame.frameWeight = 1.0f;
                        UnityEngine.Vector3[] deltaVertices = new UnityEngine.Vector3[numVertices];
                        UnityEngine.Vector3[] deltaNormals = null;
                        if (normals != null)
                        {
                            deltaNormals = new UnityEngine.Vector3[numVertices];
                        }

                        UnityEngine.Vector3[] deltaTangents = null;
                        if (tangents != null)
                        {
                            deltaTangents = new UnityEngine.Vector3[numVertices];
                        }

                        unityMesh.GetBlendShapeFrameVertices(
                            blendShapeI,
                            blendShapeFrameI,
                            deltaVertices,
                            deltaNormals,
                            deltaTangents
                        );
                        Float3_U2Res[] frameVertices = convertSubmesh
                            ? ConvertSubset<Float3_U2Res, UnityEngine.Vector3>(deltaVertices, submeshesVertexSubset)
                            : SerializationUtils.ConvertArray<Float3_U2Res, UnityEngine.Vector3>(deltaVertices);
                        /*
                        for (int i = 0; i < numVertices; i++)
                        {
                            frameVertices[i].x = frameVertices[i].x;
                            frameVertices[i].y = frameVertices[i].y;
                            frameVertices[i].z = frameVertices[i].z;
                        }
                        */
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            frameVertices[i].x *= scaleFactor;
                            frameVertices[i].y *= scaleFactor;
                            frameVertices[i].z *= scaleFactor;
                        }

                        frame.positions = frameVertices;
                        
                        if (normals != null)
                        {
                            Float3_U2Res[] frameNormals = convertSubmesh
                                ? ConvertSubset<Float3_U2Res, UnityEngine.Vector3>(deltaNormals, submeshesVertexSubset)
                                : SerializationUtils.ConvertArray<Float3_U2Res, UnityEngine.Vector3>(deltaNormals);
                            for (int i = 0; i < numVertices; i++)
                            {
                                // idk if this is right? but it doesn't go all black or dissapear anymore... maybe i need to normalize?
                                frameNormals[i].x = frameNormals[i].x + normals[i].x;
                                frameNormals[i].y = frameNormals[i].y + normals[i].y;
                                frameNormals[i].z = frameNormals[i].z + normals[i].z;
                            }
                            frame.normals = frameNormals;
                        }

                        if (tangents != null)
                        {
                            Float3_U2Res[] frameTangents = convertSubmesh
                                ? ConvertSubset<Float3_U2Res, UnityEngine.Vector3>(deltaTangents, submeshesVertexSubset)
                                : SerializationUtils.ConvertArray<Float3_U2Res, UnityEngine.Vector3>(deltaTangents);
                            for (int i = 0; i < numVertices; i++)
                            {
                                frameTangents[i].x = frameTangents[i].x; // - tangents[i].x;
                                frameTangents[i].y = frameTangents[i].y; // - tangents[i].y;
                                frameTangents[i].z = frameTangents[i].z; // - tangents[i].z;
                            }
                            frame.tangents = frameTangents;
                        }
                        blendShape.frames[blendShapeFrameI] = frame;
                    }
                    blendShapes[blendShapeI] = blendShape;
                }
                meshx.blendShapes = blendShapes;
            }

            // todo: they have some normal flipping thing if normals are wrong, do we need that?

            using (Timer _ = new Timer("Bone data"))
            {
                var numBonesPerVertex = unityMesh.GetBonesPerVertex();
                var boneWeights = unityMesh.GetAllBoneWeights();
                meshx.bones = new Bone_U2Res[boneNames.Length];
                meshx.boneBindings = new BoneBinding_U2Res[numVertices];
                UnityEngine.Matrix4x4[] boneBindposes = unityMesh.bindposes;
                if (numBonesPerVertex.Length > 0 && boneWeights.Length > 0)
                {
                    for (int boneI = 0; boneI < boneNames.Length; boneI++)
                    {
                        meshx.bones[boneI] = new Bone_U2Res();
                        meshx.bones[boneI].name = boneNames[boneI];
                        // apply rescale
                        UnityEngine.Vector3 pos = boneBindposes[boneI].GetPosition() * scaleFactor;
                        UnityEngine.Quaternion rot = boneBindposes[boneI].rotation;
                        UnityEngine.Vector3 scale = boneBindposes[boneI].lossyScale;
                        meshx.bones[boneI].bindPose = ConvertMatrix4x4(Matrix4x4.TRS(pos, rot, scale));
                    }
                    int boneWeightIndex = 0;

                    if (!convertSubmesh)
                    {
                        // unity has us traverse over all them in this weird way
                        for (int vertexI = 0; vertexI < numVertices; vertexI++)
                        {
                            byte numBones = numBonesPerVertex[vertexI];
                            BoneBinding_U2Res boneBinding = new BoneBinding_U2Res();
                            for (int boneI = 0; boneI < numBones; boneI++)
                            {
                                UnityEngine.BoneWeight1 boneWeight = boneWeights[boneWeightIndex++];
                                switch (boneI)
                                {
                                    case 0:
                                        boneBinding.boneIndex0 = boneWeight.boneIndex;
                                        boneBinding.weight0 = boneWeight.weight;
                                        break;
                                    case 1:
                                        boneBinding.boneIndex1 = boneWeight.boneIndex;
                                        boneBinding.weight1 = boneWeight.weight;
                                        break;
                                    case 2:
                                        boneBinding.boneIndex2 = boneWeight.boneIndex;
                                        boneBinding.weight2 = boneWeight.weight;
                                        break;
                                    case 3:
                                        boneBinding.boneIndex3 = boneWeight.boneIndex;
                                        boneBinding.weight3 = boneWeight.weight;
                                        break;
                                    // sadly resonite only supports up to 4 bones per vertex
                                    default:
                                        break;
                                }
                                meshx.boneBindings[vertexI] = boneBinding;
                                // luckily unity is already sorted (todo: is decending order correct?)
                            }
                        }
                    }
                    else
                    {
                        int vertexPresentI = 0;
                        // unity has us traverse over all them in this weird way
                        for (int vertexI = 0; vertexI < numVertices; vertexI++)
                        {
                            byte numBones = numBonesPerVertex[vertexI];
                            BoneBinding_U2Res boneBinding = new BoneBinding_U2Res();
                            for (int boneI = 0; boneI < numBones; boneI++)
                            {
                                UnityEngine.BoneWeight1 boneWeight = boneWeights[boneWeightIndex++];
                                switch (boneI)
                                {
                                    case 0:
                                        boneBinding.boneIndex0 = boneWeight.boneIndex;
                                        boneBinding.weight0 = boneWeight.weight;
                                        break;
                                    case 1:
                                        boneBinding.boneIndex1 = boneWeight.boneIndex;
                                        boneBinding.weight1 = boneWeight.weight;
                                        break;
                                    case 2:
                                        boneBinding.boneIndex2 = boneWeight.boneIndex;
                                        boneBinding.weight2 = boneWeight.weight;
                                        break;
                                    case 3:
                                        boneBinding.boneIndex3 = boneWeight.boneIndex;
                                        boneBinding.weight3 = boneWeight.weight;
                                        break;
                                    // sadly resonite only supports up to 4 bones per vertex
                                    default:
                                        break;
                                }
                                // only add vertices if they are present in submesh
                                if (whichVerticesPresent[vertexI])
                                {
                                    meshx.boneBindings[vertexPresentI++] = boneBinding;
                                }
                                // luckily unity is already sorted (todo: is decending order correct?)
                            }
                        }
                    }
                    //
                    //meshx.SortTrimAndNormalizeBoneWeights();
                    //meshx.FillInEmptyBindings(0);
                }
            }

            return meshx;
        }

        // resonite wants things scaled up for ik to work correctly, so do that
        public static float FIXED_SCALE_FACTOR = 100.0f;
        public static IEnumerable<object> SendMeshToResonite(HierarchyLookup hierarchyLookup, UnityEngine.Mesh mesh, string[] boneNames, Matrix4x4 submeshTransform, int subMeshStartIndex, int subMeshEndIndex, ResoniteBridgeClient bridgeClient, OutputHolder<object> output)
        {
            StaticMesh_U2Res convertedMesh = ConvertMesh(mesh, boneNames.ToArray(), submeshTransform, subMeshStartIndex, subMeshEndIndex, FIXED_SCALE_FACTOR);
            convertedMesh.rootAssetsSlot = hierarchyLookup.rootAssetsSlot;


            using (Timer _ = new Timer("Encoding"))
            {
                //byte[] encoded = SerializationUtils.EncodeObject(convertedMesh);
                // test to make sure encodes correctlyf
                //StaticMesh_U2Res decoded = SerializationUtils.DecodeObject<StaticMesh_U2Res>(encoded);
                //CheckAllEqual(convertedMesh, decoded);
            }
            using (Timer _ = new Timer("Processing Static Mesh"))
            {
                foreach (var e in hierarchyLookup.Call<RefID_U2Res, StaticMesh_U2Res>("ImportToStaticMesh", convertedMesh, output))
                {
                    yield return e;
                }
            }
        }
    }
}
