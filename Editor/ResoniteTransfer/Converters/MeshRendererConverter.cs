﻿using ResoniteUnityExporterShared;
using System.Collections.Generic;
using UnityEngine;

namespace ResoniteUnityExporter.Converters
{
    public class MeshRendererConverter
    {
        public static IEnumerable<object> ConvertMeshRenderer(MeshRenderer renderer, GameObject obj, RefID_U2Res objRefID, HierarchyLookup hierarchy, ResoniteTransferSettings settings, OutputHolder<object> output)
        {
            if (renderer.GetComponent<MeshFilter>() != null && renderer.GetComponent<MeshFilter>().sharedMesh != null)
            {
                Mesh sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Sending mesh " + sharedMesh.name;
                yield return null;
                OutputHolder<object> meshOutputHolder = new OutputHolder<object>();
                RefID_U2Res meshRefId;
                // static batching handling
                // just grab the subset
                if (renderer.isPartOfStaticBatch && renderer.sharedMaterials != null)
                {
                    // extract the relevant submesh, when static batching
                    int startIndex = renderer.subMeshStartIndex;
                    int endIndex = renderer.subMeshStartIndex + renderer.sharedMaterials.Length;
                    // also need to transform the vertices so that this object is at 0,0,0 and not rotated
                    // simplest way to do that is
                    // make empty object a 0,0,0
                    // parent to this
                    // read it's local transform and rotation
                    // we can do that by just globalToLocal

                    foreach (var meshEn in hierarchy.SendOrGetMesh(sharedMesh, new string[] { },
                        renderer.transform.worldToLocalMatrix,
                        meshOutputHolder,
                        subMeshStartIndex: startIndex,
                        subMeshEndIndexExclusive: endIndex))
                    {
                        yield return meshEn;
                    }
                    meshRefId = (RefID_U2Res)meshOutputHolder.value;
                }
                else
                {
                    foreach (var meshEn in hierarchy.SendOrGetMesh(sharedMesh, new string[] { }, Matrix4x4.identity, meshOutputHolder))
                    {
                        yield return meshEn;
                    }
                    meshRefId = (RefID_U2Res)meshOutputHolder.value;
                }

                RefID_U2Res[] materialRefIds = new RefID_U2Res[renderer.sharedMaterials.Length];
                int i = 0;
                foreach (UnityEngine.Material mat in renderer.sharedMaterials)
                {
                    if (mat != null)
                    {
                        ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Sending material " + mat.name;
                        yield return null;
                        OutputHolder<object> materialOutputHolder = new OutputHolder<object>();
                        foreach (var materialEn in hierarchy.SendOrGetMaterial(mat, materialOutputHolder))
                        {
                            yield return materialEn;
                        }
                        materialRefIds[i++] = (RefID_U2Res)materialOutputHolder.value;
                        yield return null;
                    }
                    else
                    {
                        materialRefIds[i++] = new RefID_U2Res()
                        {
                            id = 0
                        };
                    }
                }

                MeshRenderer_U2Res meshRendererData = new MeshRenderer_U2Res()
                {
                    targetSlot = objRefID,
                    staticMeshAsset = meshRefId,
                    materials = materialRefIds,
                };

                ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Creating skinned mesh renderer";
                yield return null;
                foreach (var e in hierarchy.Call<RefID_U2Res, MeshRenderer_U2Res>("ImportMeshRenderer", meshRendererData, output))
                {
                    yield return e;
                }
            }
        }
    }
}
