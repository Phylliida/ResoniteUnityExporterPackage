﻿using MemoryMappedFileIPC;
using ResoniteBridgeLib;
using ResoniteUnityExporter.Converters;
using ResoniteUnityExporterShared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Animations;
#if RUE_HAS_VRCSDK
using VRC.Dynamics;
#endif

namespace ResoniteUnityExporter
{

    public class ObjectHolder
    {
        public ObjectHolder(GameObject gameObject, RefID_U2Res slotRefId)
        {
            this.gameObject = gameObject;
            this.slotRefId = slotRefId;
        }

        public GameObject gameObject;
        public RefID_U2Res slotRefId;
    }
    public class HierarchyLookup
    {
        public RefID_U2Res rootAssetsSlot;
        public RefID_U2Res rootHierarchySlot;
        public RefID_U2Res mainParentSlot;
        public ResoniteBridgeClient bridgeClient;
        public ResoniteTransferManager transferManager;
        List<ObjectHolder> objects = new List<ObjectHolder>();
        Dictionary<string, GameObject> gameObjectLookup;
        Dictionary<string, RefID_U2Res> refIdLookup;
        Dictionary<ulong, GameObject> refIdToGameObject;
        Dictionary<string, RefID_U2Res> assetLookup = new Dictionary<string, RefID_U2Res>();

        public HierarchyLookup(ResoniteTransferManager transferManager, Dictionary<string, GameObject> gameObjectLookup, Dictionary<string, RefID_U2Res> refIdLookup, ResoniteBridgeClient bridgeClient, RefID_U2Res rootAssetsSlot, RefID_U2Res mainParentSlot)
        {
            this.transferManager = transferManager;
            refIdToGameObject = new Dictionary<ulong, GameObject>();
            foreach (KeyValuePair<string, RefID_U2Res> keyRefID in refIdLookup)
            {
                GameObject gameObject = gameObjectLookup[keyRefID.Key];
                RefID_U2Res refId = keyRefID.Value;
                refIdToGameObject[keyRefID.Value.id] = gameObject;
                objects.Add(new ObjectHolder(gameObject, refId));
            }
            this.gameObjectLookup = gameObjectLookup;
            this.refIdLookup = refIdLookup;
            this.bridgeClient = bridgeClient;
            this.rootAssetsSlot = rootAssetsSlot;
            this.mainParentSlot = mainParentSlot;
        }

        public IEnumerable<ObjectHolder> GetObjects()
        {
            foreach (ObjectHolder obj in objects)
            {
                yield return obj;
            }
        }

        public Transform LookupTransform(RefID_U2Res refID)
        {
            return refIdToGameObject[refID.id].transform;
        }

        public Transform LookupTransform(string key)
        {
            return gameObjectLookup[key].transform;
        }

        public RefID_U2Res LookupSlot(string key)
        {
            return refIdLookup[key];
        }

        public RefID_U2Res LookupSlot(Transform transform)
        {
            return refIdLookup[transform.gameObject.GetInstanceID().ToString()];
        }

        public bool TryLookupSlot(Transform transform, out RefID_U2Res slotRefID)
        {
            return refIdLookup.TryGetValue(transform.gameObject.GetInstanceID().ToString(), out slotRefID);
        }

        delegate IEnumerable<object> CreateAssetDelegate();

        IEnumerable<object> CreateAssetIfNotExist(string assetId, CreateAssetDelegate createAsset, OutputHolder<object> output)
        {
            RefID_U2Res outRefId;
            if (!assetLookup.TryGetValue(assetId, out outRefId))
            {
                foreach (var en in createAsset())
                {
                    yield return en;
                }
                outRefId = (RefID_U2Res)output.value;
                assetLookup.Add(assetId, outRefId);
            }
            output.value = outRefId;
        }

        public IEnumerable<object> Call<OutType, InType>(string callMethodName, InType input, OutputHolder<object> output)
        {
            foreach (var en in Call<OutType, InType>(bridgeClient, callMethodName, input, output))
            {
                yield return en;
            }
        }

        public class RemoteException : Exception
        {
            public string Message;
            public RemoteException(string stackTrace)
            {
                this.Message = stackTrace;
            }
        }



        public static IEnumerable<object> Call<OutType, InType>(ResoniteBridgeClient bridgeClient, string callMethodName, InType input, OutputHolder<object> output)
        {
            bool hasError = false;
            System.Threading.Tasks.TaskCompletionSource<bool> taskCompletionSource = new System.Threading.Tasks.TaskCompletionSource<bool>();
            
            async Task<object> CallServer()
            {
                try
                {
                    byte[] messageBytes = SerializationUtils.EncodeObject(input);
                    var result = await bridgeClient.SendMessage(
                           callMethodName,
                           messageBytes,
                           -1);
                    if (result.isError)
                    {
                        hasError = true;
                        return (object)SerializationUtils.DecodeString(result.messageBytes);
                    }
                    return (object)SerializationUtils.DecodeObject<OutType>(result.messageBytes);
                }
                catch (Exception e)
                {
                    hasError = true;
                    return (object)e.ToString();
                }
            }
            Task<object> asyncTask = CallServer();
            // we need to poll it so unity doesn't freeze up
            while (!asyncTask.IsCompleted && !asyncTask.IsCanceled && !asyncTask.IsFaulted)
            {
                yield return null;
            }
            var result = asyncTask.Result;
            if (hasError)
            {
                string error = (string)result;
                // don't print disconnect exceptions, just ignore them
                if (!error.Contains("DisconnectException"))
                {
                    Debug.LogError(error);
                }

                throw new RemoteException(error);
            }
            else
            {
                output.value = asyncTask.Result;
            }
        }

        public IEnumerable<object> SendOrGetMesh(UnityEngine.Mesh mesh, string[] boneNames, Matrix4x4 submeshTransform, OutputHolder<object> output, int subMeshStartIndex=-1, int subMeshEndIndexExclusive=-1)
        {
            string subMeshId = "";
            if (subMeshStartIndex != -1 && subMeshEndIndexExclusive != -1)
            {
                subMeshId = "submesh:" + subMeshStartIndex + " " + subMeshEndIndexExclusive;
            }
            foreach (var en in CreateAssetIfNotExist(mesh.GetInstanceID().ToString() + subMeshId, () =>
            {
                return ResoniteTransferMesh.SendMeshToResonite(this, mesh, boneNames, submeshTransform, subMeshStartIndex, subMeshEndIndexExclusive, bridgeClient, output);
            }, output))
            {
                yield return en;
            }
        }

        public IEnumerable<object> SendOrGetMaterial(UnityEngine.Material material, OutputHolder<object> output)
        {
            foreach (var en in CreateAssetIfNotExist(material.GetInstanceID().ToString(), () =>
            {
                return ResoniteTransferMaterial.SendMaterialToResonite(this, material, bridgeClient, output);
            }, output))
            {
                yield return en;
            }
        }

        public IEnumerable<object> SendOrGetTexture(UnityEngine.Texture2D texture, OutputHolder<object> output)
        {
            foreach (var en in CreateAssetIfNotExist(texture.GetInstanceID().ToString(), () =>
            {
                return ResoniteTransferTexture2D.SendTextureToResonite(this, texture, bridgeClient, output);
            }, output))
            {
                yield return en;
            }
        }
    }


    public class ResoniteTransferHierarchy
    {
        public static bool IsParentOrMeContainedInList(List<GameObject> objList, Transform child)
        {
            Transform parent = child;
            while (parent != null)
            {
                if (objList.Contains(parent.gameObject))
                {
                    return true;
                }
                parent = parent.parent;
            }
            return false;
        }


        public static bool IsAncestorofObj(Transform ancestor, Transform child)
        {
            Transform parent = child;
            while (parent != null)
            {
                if (parent == ancestor || parent.parent == ancestor)
                {
                    return true;
                }
                parent = parent.parent;
            }
            return false;
        }

        public static bool IsInHierarchy(GameObject[] parents, Transform obj)
        {
            foreach (GameObject parentObj in parents) { 
                if (IsAncestorofObj(parentObj.transform, obj))
                {
                    return true;
                }
            }
            return false;
        }

        public static IEnumerable<GameObject> GetAttachedObjects(GameObject rootObj)
        {
            // LOD
            foreach (LODGroup lodGroup in rootObj.GetComponentsInChildren<LODGroup>())
            {
                // add lod group references
                foreach (LOD lod in lodGroup.GetLODs())
                {
                    foreach (Renderer renderer in lod.renderers)
                    {
                        if (renderer != null)
                        {
                            yield return renderer.gameObject;
                        }
                    }
                }
            }

            // skinned mesh renderer bones
            foreach (SkinnedMeshRenderer skinnedMeshRenderer in rootObj.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skinnedMeshRenderer.bones != null)
                {
                    foreach (Transform bone in skinnedMeshRenderer.bones)
                    {
                        // if outside of hierarchy we are exporting, also include it
                        if (bone != null)
                        {
                            yield return bone.gameObject;
                        }
                    }
                }
            }

#if RUE_HAS_AVATAR_VRCSDK
            foreach (VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone physBone in rootObj.GetComponentsInChildren<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>())
            {
                foreach (PhysBoneConverter.BoneInfo bone in PhysBoneConverter.GetBones(physBone, physBone.gameObject, out int depth))
                {
                    Transform boneTransform = bone.transform;
                    if (boneTransform != null)
                    {
                        yield return boneTransform.gameObject;
                    }
                }
                if (physBone.colliders != null)
                {
                    foreach (VRC.Dynamics.VRCPhysBoneColliderBase collider in physBone.colliders)
                    {
                        if (collider != null)
                        {
                            yield return collider.gameObject;
                        }
                    }
                }
            }

            foreach (VRCConstraintBase constraint in rootObj.GetComponentsInChildren<VRCConstraintBase>())
            {
                foreach (var source in constraint.Sources)
                {
                    if (source.SourceTransform != null)
                    {
                        yield return source.SourceTransform.gameObject;
                    }
                }
            }
#endif

            // constraint sources
            foreach (PositionConstraint constraint in rootObj.GetComponentsInChildren<PositionConstraint>())
            {
                List<ConstraintSource> sources = new List<ConstraintSource>(constraint.sourceCount);
                constraint.GetSources(sources);
                foreach (ConstraintSource source in sources)
                {
                    if (source.sourceTransform != null)
                    {
                        yield return source.sourceTransform.gameObject;
                    }
                }
            }

            foreach (RotationConstraint constraint in rootObj.GetComponentsInChildren<RotationConstraint>())
            {
                List<ConstraintSource> sources = new List<ConstraintSource>(constraint.sourceCount);
                constraint.GetSources(sources);
                foreach (ConstraintSource source in sources)
                {
                    if (source.sourceTransform != null)
                    {
                        yield return source.sourceTransform.gameObject;
                    }
                }
            }



            foreach (ScaleConstraint constraint in rootObj.GetComponentsInChildren<ScaleConstraint>())
            {
                List<ConstraintSource> sources = new List<ConstraintSource>(constraint.sourceCount);
                constraint.GetSources(sources);
                foreach (ConstraintSource source in sources)
                {
                    if (source.sourceTransform != null)
                    {
                        yield return source.sourceTransform.gameObject;
                    }
                }
            }

            foreach (ParentConstraint constraint in rootObj.GetComponentsInChildren<ParentConstraint>())
            {
                List<ConstraintSource> sources = new List<ConstraintSource>(constraint.sourceCount);
                constraint.GetSources(sources);
                foreach (ConstraintSource source in sources)
                {
                    if (source.sourceTransform != null)
                    {
                        yield return source.sourceTransform.gameObject;
                    }
                }
            }

            foreach (LookAtConstraint constraint in rootObj.GetComponentsInChildren<LookAtConstraint>())
            {
                List<ConstraintSource> sources = new List<ConstraintSource>(constraint.sourceCount);
                constraint.GetSources(sources);
                foreach (ConstraintSource source in sources)
                {
                    if (source.sourceTransform != null)
                    {
                        yield return source.sourceTransform.gameObject;
                    }
                }
            }

            foreach (AimConstraint constraint in rootObj.GetComponentsInChildren<AimConstraint>())
            {
                List<ConstraintSource> sources = new List<ConstraintSource>(constraint.sourceCount);
                constraint.GetSources(sources);
                foreach (ConstraintSource source in sources)
                {
                    if (source.sourceTransform != null)
                    {
                        yield return source.sourceTransform.gameObject;
                    }
                }
            }
        }


        public static IEnumerable<object> CreateHierarchy(ResoniteTransferManager manager, string hierarchyName, Transform rootTransform, ResoniteBridgeClient bridgeClient, OutputHolder<HierarchyLookup> output)
        {
            // if rootTransform is null, grab all objects in scene
            GameObject[] gameObjects = (rootTransform == null)
                ? UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                // otherwise, just do the given object as root
                : new GameObject[] { rootTransform.gameObject };

            List<GameObject> bonusObjectsOutsideHierarchy = new List<GameObject>();
            // extra objects references by bones in skinned mesh renderers but outside hierarchy, put them at root level
            // also extra objects references by dyn bone chain colliders
            bool foundSomething = true;
            // repeat until we've crawled to grab all attached things
            while (foundSomething)
            {
                List<GameObject> curRootObjects = new List<GameObject>(gameObjects);
                curRootObjects.AddRange(bonusObjectsOutsideHierarchy);
                foundSomething = false;
                foreach (GameObject rootObj in curRootObjects)
                {
                    foreach (GameObject attachedObject in GetAttachedObjects(rootObj))
                    {
                        if (attachedObject != null
                            && !IsInHierarchy(gameObjects, attachedObject.transform)
                            && !IsParentOrMeContainedInList(bonusObjectsOutsideHierarchy, attachedObject.transform))
                        {
                            foundSomething = true;
                            bonusObjectsOutsideHierarchy.Add(attachedObject);
                        }
                    }
                }
            }

            Dictionary<string, GameObject> gameObjectLookup = new Dictionary<string, GameObject>();

            if (bonusObjectsOutsideHierarchy.Count > 0)
            {
                Debug.LogWarning("Got: " + bonusObjectsOutsideHierarchy.Count + " extra slots outside hierarchy that we need to copy (and their children)");
                Debug.LogWarning("Consider moving these inside the hierarchy for best results");
                foreach (GameObject bonusObj in bonusObjectsOutsideHierarchy)
                {
                    Debug.LogWarning("Extra slot: " + bonusObj.name);
                }
                ResoniteUnityExporterEditorWindow.DebugProgressStringDetail = "Also copying: " + bonusObjectsOutsideHierarchy.Count + " bonus bone slots outside hierarchy, using their global data";
            }
            Object_U2Res[] convertedObjects = new Object_U2Res[gameObjects.Length + bonusObjectsOutsideHierarchy.Count];
            for (int i = 0; i < gameObjects.Length; i++)
            {
                convertedObjects[i] = GameObjectToObject(gameObjects[i], gameObjectLookup);
            }
            for (int i = 0; i < bonusObjectsOutsideHierarchy.Count; i++)
            {
                convertedObjects[i + gameObjects.Length] = GameObjectToObject(bonusObjectsOutsideHierarchy[i], gameObjectLookup, useGlobal: true);
            }

            ObjectHierarchy_U2Res hierarchyData = new ObjectHierarchy_U2Res()
            {
                hierarchyName = hierarchyName,
                rescaleFactor = ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                objects = convertedObjects
            };

            byte[] encoded = SerializationUtils.EncodeObject(hierarchyData);

            OutputHolder<object> outputLookups = new OutputHolder<object>();

            foreach (var en in HierarchyLookup.Call<ObjectLookups_U2Res, ObjectHierarchy_U2Res>(
                bridgeClient,
                "ImportSlotHierarchy",
                hierarchyData,
                outputLookups))
            {
                yield return en;
            }

            ObjectLookups_U2Res lookups = (ObjectLookups_U2Res)outputLookups.value;

            Dictionary<string, RefID_U2Res> refIdLookup = new Dictionary<string, RefID_U2Res>();
            foreach (ObjectLookup_U2Res lookup in lookups.lookups)
            {
                refIdLookup.Add(lookup.uniqueId, lookup.refId);
            }

            output.value = new HierarchyLookup(manager, gameObjectLookup, refIdLookup, bridgeClient, lookups.rootAssetSlot, lookups.mainParentSlot);
        }

        public static Object_U2Res GameObjectToObject(UnityEngine.GameObject obj, Dictionary<string, GameObject> gameObjectLookup, bool useGlobal=false, bool addChildren=true)
        {
            gameObjectLookup[obj.GetInstanceID().ToString()] = obj;
            Object_U2Res[] children;
            if (addChildren) {
                children = new Object_U2Res[obj.transform.childCount];
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    children[i] = GameObjectToObject(obj.transform.GetChild(i).gameObject, gameObjectLookup, useGlobal: false, addChildren: true);
                }
            }
            else
            {
                children = new Object_U2Res[0];
            }
            
            Object_U2Res result = new Object_U2Res()
            {
                // note that this is unique during a session, but changes each time you reboot unity
                uniqueId = obj.GetInstanceID().ToString(),
                children = children,
                name = obj.name,
                enabled = obj.activeInHierarchy,
                localPosition = new Float3_U2Res()
                {
                    x = (useGlobal ? obj.transform.position.x : obj.transform.localPosition.x) * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    y = (useGlobal ? obj.transform.position.y : obj.transform.localPosition.y) * ResoniteTransferMesh.FIXED_SCALE_FACTOR,
                    z = (useGlobal ? obj.transform.position.z : obj.transform.localPosition.z) * ResoniteTransferMesh.FIXED_SCALE_FACTOR
                },
                localRotation = new Float4_U2Res()
                {
                    x = (useGlobal ? obj.transform.rotation.x : obj.transform.localRotation.x),
                    y = (useGlobal ? obj.transform.rotation.y : obj.transform.localRotation.y),
                    z = (useGlobal ? obj.transform.rotation.z : obj.transform.localRotation.z),
                    w = (useGlobal ? obj.transform.rotation.w : obj.transform.localRotation.w)
                },
                localScale = new Float3_U2Res()
                {
                    x = (useGlobal ? obj.transform.lossyScale.x : obj.transform.localScale.x),
                    y = (useGlobal ? obj.transform.lossyScale.y : obj.transform.localScale.y),
                    z = (useGlobal ? obj.transform.lossyScale.z : obj.transform.localScale.z),
                }
            };
            return result;
        }
    }
}
