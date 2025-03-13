

using ResoniteUnityExporterShared;
using System.Collections.Generic;
using UnityEngine;

namespace ResoniteUnityExporter.Converters
{
    public class ColliderConverter
    {
        public static IEnumerable<object> ConvertSphereCollider(SphereCollider sphereCollider, GameObject obj, RefID_U2Res objRefID, HierarchyLookup hierarchy, ResoniteTransferSettings settings, OutputHolder<object> output)
        {
            ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Sending sphere collider on " + obj.name;
            SphereCollider_U2Res sphereColliderData = new SphereCollider_U2Res()
            {
                target = objRefID,
                center = new Float3_U2Res()
                {
                    x = sphereCollider.center.x * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    y = sphereCollider.center.y * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    z = sphereCollider.center.z * ResoniteTransferMesh.FIXED_SCALE_FACTOR
                },
                radius = sphereCollider.radius * ResoniteTransferMesh.FIXED_SCALE_FACTOR
            };

            foreach (var en in hierarchy.Call<RefID_U2Res, SphereCollider_U2Res>("ImportSphereCollider", sphereColliderData, output))
            {
                yield return en;
            }
        }

        public static IEnumerable<object> ConvertBoxCollider(BoxCollider boxCollider, GameObject obj, RefID_U2Res objRefID, HierarchyLookup hierarchy, ResoniteTransferSettings settings, OutputHolder<object> output)
        {
            ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Sending box collider on " + obj.name;
            BoxCollider_U2Res boxColliderData = new BoxCollider_U2Res()
            {
                target = objRefID,
                center = new Float3_U2Res()
                {
                    x = boxCollider.center.x * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    y = boxCollider.center.y * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    z = boxCollider.center.z * ResoniteTransferMesh.FIXED_SCALE_FACTOR
                },
                size = new Float3_U2Res()
                {
                    x = boxCollider.size.x * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    y = boxCollider.size.y * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    z = boxCollider.size.z * ResoniteTransferMesh.FIXED_SCALE_FACTOR
                },
            };

            foreach (var en in hierarchy.Call<RefID_U2Res, BoxCollider_U2Res>("ImportBoxCollider", boxColliderData, output))
            {
                yield return en;
            }
        }


        public static IEnumerable<object> ConvertCapsuleCollider(CapsuleCollider capsuleCollider, GameObject obj, RefID_U2Res objRefID, HierarchyLookup hierarchy, ResoniteTransferSettings settings, OutputHolder<object> output)
        {
            ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Sending capsule collider on " + obj.name;
            CapsuleCollider_U2Res capsuleColliderData = new CapsuleCollider_U2Res()
            {
                target = objRefID,
                center = new Float3_U2Res()
                {
                    x = capsuleCollider.center.x * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    y = capsuleCollider.center.y * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    z = capsuleCollider.center.z * ResoniteTransferMesh.FIXED_SCALE_FACTOR
                },
                direction = (CapsuleColliderDirection_U2Res) capsuleCollider.direction,
                height = capsuleCollider.height * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                radius = capsuleCollider.radius * ResoniteTransferMesh.FIXED_SCALE_FACTOR
            };

            foreach (var en in hierarchy.Call<RefID_U2Res, CapsuleCollider_U2Res>("ImportCapsuleCollider", capsuleColliderData, output))
            {
                yield return en;
            }
        }


        public static IEnumerable<object> ConvertMeshCollider(MeshCollider meshCollider, GameObject obj, RefID_U2Res objRefID, HierarchyLookup hierarchy, ResoniteTransferSettings settings, OutputHolder<object> output)
        {
            if (meshCollider.sharedMesh != null)
            {
                OutputHolder<object> meshAssetRefIDHolder = new OutputHolder<object>();
                // this by default might ignore bones that would be used otherwise if they have skinned mesh
                // if this is processed before that skinned mesh so it gets empty bones
                // but having mesh collider for skinned mesh is cursed don't do that
                // so its probably ok
                foreach (var meshEn in hierarchy.SendOrGetMesh(meshCollider.sharedMesh, new string[] { }, Matrix4x4.identity, meshAssetRefIDHolder))
                {
                    yield return meshEn;   
                }
                RefID_U2Res meshAsset = (RefID_U2Res)meshAssetRefIDHolder.value;
                ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Sending mesh collider on " + obj.name;
                MeshCollider_U2Res meshColliderData = new MeshCollider_U2Res()
                {
                    target = objRefID,
                    staticMesh = meshAsset,
                    center = new Float3_U2Res()
                    {
                        x = 0,
                        y = 0,
                        z = 0
                    },
                    convex = meshCollider.convex,
                };

                foreach (var en in hierarchy.Call<RefID_U2Res, MeshCollider_U2Res>("ImportMeshCollider", meshColliderData, output))
                {
                    yield return en;
                }
            }
        }


    }
}