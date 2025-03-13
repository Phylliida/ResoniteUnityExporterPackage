using ResoniteUnityExporterShared;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if RUE_HAS_VRCSDK
using VRC.Core;
#endif

namespace ResoniteUnityExporter.Converters
{
    public class PipelineManagerConverter
    {
#if RUE_HAS_VRCSDK
        // we could use vrc avatar descriptor, however some old avatars don't have that,
        // whereas pipeline manager is very common
        public static IEnumerable<object> ConvertPipelineManager(PipelineManager pipelineManager, GameObject obj, RefID_U2Res objRefId, HierarchyLookup hierarchy, ResoniteTransferSettings settings, OutputHolder<object> output)
        {
            // not sending avatar, bail
            if (settings.makeAvatar)
            {
                ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "";
                // fetch all SkinnedMeshRenderer and MeshRenderer ref ids
                OutputHolder<object[]> refIdsSkinned = new OutputHolder<object[]>();
                foreach (var e in hierarchy.transferManager.LookupAllComponentsOfType<SkinnedMeshRenderer>(refIdsSkinned))
                {
                    yield return e;
                }
                OutputHolder<object[]> refIdsUnskinned = new OutputHolder<object[]>();
                foreach (var eu in hierarchy.transferManager.LookupAllComponentsOfType<MeshRenderer>(refIdsUnskinned))
                {
                    yield return eu;
                }
                List<object> refIds = new List<object>(refIdsSkinned.value);
                refIds.AddRange(refIdsUnskinned.value);

                RefID_U2Res[] rendererRefIds = refIds
                    .Where(x => x != null)
                    .Select(x => (RefID_U2Res)x).ToArray();

                bool hasCustomHeadPosition = false;
                Avatar_U2Res avatarData;
                Float3_U2Res customHeadPosition = new Float3_U2Res()
                {
                    x = 0,
                    y = 0,
                    z = 0
                };

                if(ResoniteTransferUtils.TryGetHeadPosition(hierarchy.transferManager.rootTransform, out bool foundHead, out Vector3 headPosition, out GameObject headTransform))
                {
                    hasCustomHeadPosition = true;
                    customHeadPosition = new Float3_U2Res()
                    {
                        x = headPosition.x * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                        y = headPosition.y * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                        z = headPosition.z * ResoniteTransferMesh.FIXED_SCALE_FACTOR
                    };
                }
                avatarData = new Avatar_U2Res()
                {
                    mainParentSlot = hierarchy.mainParentSlot,
                    renderers = rendererRefIds,
                    floorOnOrigin = false,
                    assetsSlot = hierarchy.rootAssetsSlot,
                    forceTPose = false,
                    generateColliders = true,
                    generateSkeletonBoneVisuals = false,
                    setupIK = settings.setupIK,
                    setupAvatarCreator = settings.setupAvatarCreator,
                    rescale = true,
                    targetScale = 1.3f,
                    nearClip = settings.nearClip,
                    hasCustomHeadPosition = hasCustomHeadPosition,
                    customHeadPosition = customHeadPosition,
                };

                yield return null;
                ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Creating avatar";
                yield return null;
                foreach (var eout in hierarchy.Call<RefID_U2Res, Avatar_U2Res>("ImportAvatar", avatarData, output))
                {
                    yield return eout;
                }
                yield return null;              
            }
        }
#endif
    }
}
