﻿using Kosmos.Prototype.Parts;
using Kosmos.Prototype.Parts.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;
using System.IO;
using UnityEngine;
using Kosmos.Prototype.Parts.Serialization;
using Kosmos.Prototype.Parts.TraitComponents;
using Mesh = UnityEngine.Mesh;
using Material = UnityEngine.Material;

namespace Assets.Scripts.Prototype.Parts
{
    //This is the code that creates the ECS representation of a vehicle from its definition
    public static class EcsVehicleSpawner
    {

        public static async Task ConstructPlayableVehicle(VehicleSpec spec)
        {
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            var vehicleEntity = entityManager.CreateEntity();

            LocalTransform vehicleLocal = new()
            {
                Position = default,
                Scale = 1,
                Rotation = default
            };
            entityManager.AddComponentData(vehicleEntity, vehicleLocal);
            entityManager.AddComponentData(vehicleEntity, new LocalToWorld());

#if UNITY_EDITOR
            entityManager.SetName(vehicleEntity, "Vehicle");
#endif

            List<Entity> parts = new();
            foreach (var partInstance in spec.Parts)
            {
                //Create entity and parent to the vehicle
                var partEntity = entityManager.CreateEntity();
                parts.Add(partEntity);
                //Load the part spec
                var partDef = PartDictionary.GetPart(Guid.Parse(partInstance.PartDefGuid));
                var partPrefab = PartDictionary.GetPartPrefabData(partDef);

#if UNITY_EDITOR
                entityManager.SetName(partEntity, partDef.Name);
#endif
                //Initialise the transform hierarchy
                Dictionary<int, Entity> hierarchy = new()
                {
                    [-1] = partEntity
                };
                CreateTransforms(partPrefab, hierarchy, entityManager);
                
                //Attach to the vehicle
                LocalTransform partLocal = new()
                {
                    Position = partInstance.LocalPosition,
                    Rotation = partInstance.LocalRotation,
                    Scale = 1
                };
                entityManager.AddComponentData(partEntity, partLocal);

                Parent parent;
                parent.Value = vehicleEntity;
                entityManager.AddComponentData(partEntity, parent);
                
                AddTraits(partInstance, partPrefab, partEntity, entityManager);
                
                await CreateMeshForEntity(partPrefab, entityManager, hierarchy);
            }

            //Setup staging
            AddStageBuffer(spec.StagingGroups, vehicleEntity, parts, entityManager);
        }

        private static void AddTraits(PartSpec partInstance, PartPrefabData partPrefab, Entity partEntity, EntityManager entityManager)
        {
            foreach (var trait in partPrefab.Traits)
            {
                var factory = TraitDictionary.GetFactoryForTrait(trait.Type);
                
                //TODO - Look up the tweakables for this part and pass them in here! 
                factory.DeserializeEcs(trait.JsonString, null, partEntity, ref entityManager);
            }
        }

        private static void CreateTransforms(PartPrefabData part, Dictionary<int, Entity> hierarchy, EntityManager entityManager)
        {
            for (var transIdx = 0; transIdx < part.Transforms.Count; transIdx++)
            {
                var transform = part.Transforms[transIdx];
                var transEntity = entityManager.CreateEntity();
                hierarchy[transIdx] = transEntity;
                
#if UNITY_EDITOR
                entityManager.SetName(transEntity, $"Transform {transIdx}");
#endif
                
                LocalTransform partLocal = new()
                {
                    Position = transform.LocalPosition,
                    Rotation = transform.LocalRotation,
                    Scale = transform.LocalScale.x
                };
                entityManager.AddComponentData(transEntity, partLocal);

                Parent parent;
                parent.Value = hierarchy[transform.ParentTransform];
                entityManager.AddComponentData(transEntity, parent);
            }
        }

        private static void AddStageBuffer(List<StagingGroup> stageGroups, Entity vehicleEntity, List<Entity> parts, EntityManager entityManager)
        {
            var stagesBuffer = entityManager.AddBuffer<Stage>(vehicleEntity);
            foreach (var stageGroup in stageGroups)
            {
                NativeArray<StagePart> stage = new NativeArray<StagePart>(stageGroup.Parts.Count(), Allocator.Persistent);
                for (var index = 0; index < stageGroup.Parts.Count; index++)
                {
                    var partIndex = stageGroup.Parts[index];
                    stage[index] = new StagePart { Value = parts[partIndex] };
                }

                stagesBuffer.Add(new Stage { Parts = stage });
            }
        }

        private static async Task CreateMeshForEntity(PartPrefabData part, EntityManager entityManager, Dictionary<int, Entity> hierarchy)
        {
            foreach (var modelDesc in part.Models)
            {
                var modelRequest = Resources.LoadAsync(Path.ChangeExtension(modelDesc.ModelPath, null));
                await modelRequest;

                var rends = (modelRequest.asset as GameObject).GetComponentsInChildren<MeshRenderer>();

                foreach (var instance in modelDesc.Instances)
                {
                    var entity = hierarchy[instance.TransformIndex];
                    var meshRend = rends.First((x) => x.GetComponent<MeshFilter>().sharedMesh.name == instance.MeshName);
                    
                    var meshRefs = new UnityObjectRef<Mesh>[1];
                    meshRefs[0] = meshRend.GetComponent<MeshFilter>().sharedMesh;

                    var materialRefs = new UnityObjectRef<Material>[meshRend.sharedMaterials.Length];
                    var materialMeshIndices = new MaterialMeshIndex[meshRend.sharedMaterials.Length];
                    
                    for (int matIdx = 0; matIdx < meshRend.sharedMaterials.Length; matIdx++)
                    {
                        materialRefs[matIdx] = meshRend.sharedMaterials[matIdx];
                        materialMeshIndices[matIdx] = new MaterialMeshIndex() { MaterialIndex = matIdx };   //TODO - WTF does this do?
                    }

                    var renderMeshDescription = new RenderMeshDescription()
                    {
                        FilterSettings = RenderFilterSettings.Default,
                        LightProbeUsage = LightProbeUsage.Off
                    };

                    var renderMeshArray = new RenderMeshArray(
                        new ReadOnlySpan<UnityObjectRef<Material>>(materialRefs),
                        new ReadOnlySpan<UnityObjectRef<Mesh>>(meshRefs),
                        new ReadOnlySpan<MaterialMeshIndex>(materialMeshIndices)
                        );

                    RenderMeshUtility.AddComponents(
                        entity,
                        entityManager,
                        renderMeshDescription,
                        renderMeshArray,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                }
            }
            
        }
    }
}