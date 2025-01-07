
namespace AdminShell
{
    using AAS2Nodeset;
    using Opc.Ua;
    using Opc.Ua.Export;
    using Opc.Ua.Server;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public class I4AASNodeManager : CustomNodeManager2
    {
        const int c_administrationNodeId = 1001;
        const int c_referableTypeNodeId = 2001;
        const int c_identifiableTypeNodeId = 2000;
        const int c_referenceNodeId = 1005;
        const int c_dataSpecificationNodeId = 3000;
        const int c_submodelElementNodeId = 1008;
        const int c_submodelNodeId = 1007;
        const int c_conceptDescriptionNodeId = 1021;
        const int c_assetNodeId = 1023;

        private ushort _namespaceIndex;
        private long _lastUsedId;

        public NodeState? _rootAAS = null;
        public NodeState? _rootConceptDescriptions = null;
        public NodeState? _rootMissingDictionaryEntries = null;

        public I4AASNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            List<string> namespaceUris =
            [
                "http://opcfoundation.org/UA/i4aas/"
            ];

            NamespaceUris = namespaceUris;

            _namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[0]);

            _lastUsedId = 0;
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            // for new nodes we create, pick our default namespace
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/i4aas/"));
        }

        public void SaveNodestateCollectionAsNodeSet2(ISystemContext context, NodeStateCollection nsc, Stream stream, bool filterSingleNodeIds)
        {
            Opc.Ua.Export.UANodeSet nodeSet = new()
            {
                LastModified = DateTime.UtcNow,
                LastModifiedSpecified = true
            };

            foreach (var n in nsc)
            {
                nodeSet.Export(context, n);
            }

            nodeSet.Write(stream);
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                IList<IReference>? objectsFolderReferences = null;
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out objectsFolderReferences))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = objectsFolderReferences = new List<IReference>();
                }

                AddNodesFromNodesetXml("./I4AAS.NodeSet2.xml");

                _rootAAS = CreateFolder(null, "AASROOT");
                objectsFolderReferences.Add(new NodeStateReference(ReferenceTypes.Organizes, false, _rootAAS.NodeId));

                _rootConceptDescriptions = CreateFolder(_rootAAS, "Concept Descriptions");
                _rootMissingDictionaryEntries = CreateFolder(_rootAAS, "Dictionary Entries");

                if (Program.g_AASEnv != null)
                {
                    CreateObjects(Program.g_AASEnv);

                    if (!Path.Exists(Path.Combine(Directory.GetCurrentDirectory(), "NodeSets")))
                    {
                        Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "NodeSets"));
                    }

                    string c_exportFilename = Path.Combine(Directory.GetCurrentDirectory(), "NodeSets", Program.g_AASEnv.AssetAdministrationShells[0].IdShort + ".NodeSet2.xml");

                    try
                    {
                        NodeStateCollection nodesToExport = new();
                        foreach (NodeState node in PredefinedNodes.Values)
                        {
                            // only export nodes belonging to the I4AAS namespace
                            if (node.NodeId.NamespaceIndex != _namespaceIndex)
                            {
                                continue;
                            }

                            nodesToExport.Add(node);
                        }

                        // export nodeset XML
                        Console.WriteLine("Writing Nodeset2 file: " + c_exportFilename);

                        using (var stream = new StreamWriter(c_exportFilename))
                        {
                            SaveNodestateCollectionAsNodeSet2(SystemContext, nodesToExport, stream.BaseStream, false);
                        }

                        Console.WriteLine();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message + " when exporting to {0}", c_exportFilename);
                    }
                }

                AddReverseReferences(externalReferences);
                base.CreateAddressSpace(externalReferences);
            }
        }

        private void AddNodesFromNodesetXml(string nodesetFile)
        {
            using (Stream stream = new FileStream(nodesetFile, FileMode.Open))
            {
                UANodeSet nodeSet = UANodeSet.Read(stream);

                NodeStateCollection predefinedNodes = new NodeStateCollection();

                nodeSet.Import(SystemContext, predefinedNodes);

                for (int i = 0; i < predefinedNodes.Count; i++)
                {
                    try
                    {
                        AddPredefinedNode(SystemContext, predefinedNodes[i]);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message, ex);
                    }
                }
            }
        }

        public static Referable? FindReferableByReference(AssetAdministrationShellEnvironment environment, Reference? reference, int keyIndex = 0)
        {
            if (environment == null || reference == null)
            {
                return null;
            }

            var keyList = reference?.Keys;
            if (keyList == null || keyList.Count == 0 || keyIndex >= keyList.Count)
            {
                return null;
            }

            var firstKeyType = keyList[keyIndex].Type;
            var firstKeyId = keyList[keyIndex].Value;

            switch (firstKeyType)
            {
                case KeyElements.AssetAdministrationShell:
                {
                    var aas = environment.AssetAdministrationShells.Where(
                        shell => shell.IdShort.Equals(firstKeyId,
                        StringComparison.OrdinalIgnoreCase)).First();

                    if (aas == null || keyIndex >= keyList.Count - 1)
                    {
                        return aas;
                    }

                    return FindReferableByReference(environment, reference, ++keyIndex);
                }

                case KeyElements.GlobalReference:
                {
                    var keyedAas = environment.AssetAdministrationShells.Where(
                        globalRef => globalRef.AssetInformation.GlobalAssetId.Equals(firstKeyId,
                        StringComparison.OrdinalIgnoreCase)).First();

                    if (keyedAas != null)
                    {
                        return keyedAas;
                    }

                    return null;
                }

                case KeyElements.ConceptDescription:
                {
                    var keyedAas = environment.ConceptDescriptions.Where(
                        description => description.IdShort.Equals(firstKeyId,
                        StringComparison.OrdinalIgnoreCase)).First();

                    if (keyedAas != null)
                    {
                        return keyedAas;
                    }

                    return null;
                }

                case KeyElements.Submodel:
                {
                    var submodel = environment.Submodels.Where(
                        description => description.IdShort.Equals(firstKeyId,
                        StringComparison.OrdinalIgnoreCase)).First();

                    if (submodel == null)
                    {
                        return null;
                    }

                    if (keyIndex >= keyList.Count - 1)
                    {
                        return submodel;
                    }

                    return FindReferableByReference(environment, reference, ++keyIndex);
                }
            }

            return null;
        }

        public void CreateObjects(AssetAdministrationShellEnvironment env)
        {
            if (_rootAAS == null)
            {
                return;
            }

            if (env.ConceptDescriptions != null && _rootConceptDescriptions != null)
            {
                foreach (ConceptDescription cd in env.ConceptDescriptions)
                {
                    CreateVariable<string>(_rootConceptDescriptions, cd.Identification.Id, c_conceptDescriptionNodeId, (cd.Description.Count > 0)? cd.Description[0]?.Text : string.Empty);
                }
            }

            if (env.AssetAdministrationShells != null)
            {
                foreach (var aas in env.AssetAdministrationShells)
                {
                    CreateObject(_rootAAS, env, aas);
                }
            }
        }

        public NodeState? CreateObject(NodeState parent, AssetAdministrationShellEnvironment env, AssetAdministrationShell aas)
        {
            if (env == null || aas == null)
            {
                return null;
            }

            var o = CreateFolder(parent, "AssetAdministrationShell_" + aas.IdShort);

            CreateVariable<string>(o, "Referable", c_referableTypeNodeId, aas.Identification.Value);
            CreateVariable<string>(o, "Identification", c_identifiableTypeNodeId, aas.Id);
            CreateVariable<string>(o, "Administration", c_administrationNodeId, aas.Administration?.ToString());

            if (aas.EmbeddedDataSpecifications != null)
            {
                foreach (var ds in aas.EmbeddedDataSpecifications)
                {
                    CreateVariable<string>(o, "DataSpecification", c_dataSpecificationNodeId, ds.DataSpecification.ToString());
                }
            }

            CreateVariable<string>(o, "DerivedFrom", c_referenceNodeId, aas.DerivedFrom.ToString());

            if (aas.AssetInformation != null)
            {
                CreateVariable<string>(o, "Asset", c_assetNodeId, aas.AssetInformation.ToString());
            }

            if (aas.Submodels != null && aas.Submodels.Count > 0)
            {
                for (int i = 0; i < aas.Submodels.Count; i++)
                {
                    CreateVariable<string>(o, "Submodel Reference " + i.ToString(), c_submodelNodeId, aas.Submodels[i].Keys[0].Value);
                }
            }

            if (env.Submodels != null && env.Submodels.Count > 0)
            {
                for (int i = 0; i < env.Submodels.Count; i++)
                {
                    NodeState sm = CreateFolder(o, "Submodel Definition " + i.ToString() + "_" + env.Submodels[i].IdShort);

                    if (env.Submodels[i].SubmodelElements.Count > 0)
                    {
                        foreach (SubmodelElementWrapper smew in env.Submodels[i].SubmodelElements)
                        {
                            CreateSubmodelElement(sm, smew.SubmodelElement);
                        }
                    }
                }
            }

            return o;
        }

        private void CreateSubmodelElement(NodeState parent, SubmodelElement sme)
        {
            if (sme is SubmodelElementCollection collection)
            {
                if (collection.Value != null)
                {
                    NodeState collectionFolder = CreateFolder(parent, "Submodel Elements");

                    foreach (SubmodelElementWrapper smew in collection.Value)
                    {
                        CreateSubmodelElement(collectionFolder, smew.SubmodelElement);
                    }
                }
            }
            else
            {
                if (sme is Property)
                {
                    CreateVariable<string>(parent, sme.IdShort, c_submodelElementNodeId, ((Property)sme).Value);
                }
                else if (sme is Blob)
                {
                    CreateVariable<string>(parent, sme.IdShort, c_submodelElementNodeId, ((Blob)sme).Value);
                }
                else if (sme is File)
                {
                    CreateVariable<string>(parent, sme.IdShort, c_submodelElementNodeId, ((File)sme).Value);
                }
                else
                {
                    CreateVariable<string>(parent, sme.IdShort, c_submodelElementNodeId, string.Empty);
                }
            }
        }

        public FolderState CreateFolder(NodeState? parent, string browseDisplayName)
        {
            FolderState x = new(parent)
            {
                BrowseName = browseDisplayName,
                DisplayName = browseDisplayName,
                NodeId = new NodeId(browseDisplayName, _namespaceIndex),
                TypeDefinitionId = ObjectTypeIds.FolderType
            };

            AddPredefinedNode(SystemContext, x);

            if (parent != null)
            {
                parent.AddChild(x);
            }

            return x;
        }

        public BaseDataVariableState<T> CreateVariable<T>(
            NodeState parent,
            string browseDisplayName,
            NodeId dataTypeId,
            T value,
            NodeId? referenceTypeFromParentId = null,
            NodeId? typeDefinitionId = null,
            int valueRank = -2,
            bool defaultSettings = false)
        {
            if (defaultSettings)
            {
                referenceTypeFromParentId = ReferenceTypeIds.HasProperty;
                typeDefinitionId = VariableTypeIds.PropertyType;
                if (valueRank == -2)
                {
                    valueRank = -1;
                }
            }

            BaseDataVariableState<T> x = new(parent)
            {
                BrowseName = browseDisplayName,
                DisplayName = browseDisplayName,
                Description = new Opc.Ua.LocalizedText("en", browseDisplayName),
                DataType = dataTypeId
            };

            if (valueRank > -2)
            {
                x.ValueRank = valueRank;
            }

            x.Value = (T)value;
            x.NodeId = new NodeId(browseDisplayName, _namespaceIndex);

            AddPredefinedNode(SystemContext, x);

            if (parent != null)
            {
                parent.AddChild(x);
            }

            if (referenceTypeFromParentId != null)
            {
                if (parent != null)
                {
                    if (!parent.ReferenceExists(referenceTypeFromParentId, false, x.NodeId))
                    {
                        parent.AddReference(referenceTypeFromParentId, false, x.NodeId);
                    }

                    if (referenceTypeFromParentId == ReferenceTypeIds.HasComponent)
                    {
                        x.AddReference(referenceTypeFromParentId, true, parent.NodeId);
                    }

                    if (referenceTypeFromParentId == ReferenceTypeIds.HasProperty)
                    {
                        x.AddReference(referenceTypeFromParentId, true, parent.NodeId);
                    }
                }
            }

            if (typeDefinitionId != null)
            {
                x.TypeDefinitionId = typeDefinitionId;
            }

            x.AccessLevel = AccessLevels.CurrentReadOrWrite;
            x.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            return x;
        }
    }
}
