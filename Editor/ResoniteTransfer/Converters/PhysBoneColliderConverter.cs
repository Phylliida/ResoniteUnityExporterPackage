using ResoniteUnityExporterShared;
using System.Collections.Generic;
using UnityEngine;
#if RUE_HAS_VRCSDK
using VRC.SDK3.Dynamics.PhysBone.Components;
#endif

namespace ResoniteUnityExporter.Converters
{
    public class PhysBoneColliderConverter
    {
#if RUE_HAS_VRCSDK
        public static IEnumerable<object> ConvertPhysBoneCollider(VRCPhysBoneCollider physBoneCollider, GameObject obj, RefID_U2Res objRefID, HierarchyLookup hierarchy, ResoniteTransferSettings settings, OutputHolder<object> output)
        {
            DynamicBoneCollider_U2Res boneChainColliderData = new DynamicBoneCollider_U2Res()
            {
                targetSlot = objRefID,
                colliderType = physBoneCollider.shapeType.ToString(),
                radius = physBoneCollider.radius * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                height = physBoneCollider.height * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                localPosition = new Float3_U2Res()
                {
                    x = physBoneCollider.position.x*ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    y = physBoneCollider.position.y * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    z = physBoneCollider.position.z * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                },
                localRotation = new Float4_U2Res()
                {
                    x = physBoneCollider.rotation.x,
                    y = physBoneCollider.rotation.y,
                    z = physBoneCollider.rotation.z,
                    w = physBoneCollider.rotation.w,
                }
            };

            yield return null;
            ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Creating dynamic bone collider";
            yield return null;
            foreach (var e in hierarchy.Call<RefID_U2Res[], DynamicBoneCollider_U2Res>("ImportDynamicBoneCollider", boneChainColliderData, output))
            {
                yield return e;
            }
        }
#endif
    }
}
