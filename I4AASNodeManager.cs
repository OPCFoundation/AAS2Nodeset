
namespace AdminShell
{
    using AAS2Nodeset;
    using Opc.Ua;
    using Opc.Ua.Export;
    using Opc.Ua.Server;
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class I4AASNodeManager : CustomNodeManager2
    {
        private long _lastUsedId = 0;

        private string _namespaceURI = "http://opcfoundation.org/UA/AAS2Nodeset/";

        public FolderState? _rootAssetAdminShells = null;
        public FolderState? _rootSubmodels = null;
        public FolderState? _rootConceptDescriptions = null;

        public I4AASNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            List<string> namespaceUris = new();

            if (!string.IsNullOrEmpty(Program.g_AASEnv?.AssetAdministrationShells[0].IdShort))
            {
                if (Program.g_AASEnv.AssetAdministrationShells[0].IdShort == "defaultAdminShell")
                {
                    _namespaceURI = "http://catena-x.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].Id.Replace("/", "_").Replace(":", "_").Replace("urn:", "").Replace(".", "_").Replace("#", "_") + "/";
                }
                else
                {
                    _namespaceURI = "http://industrialdigitaltwin.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].IdShort.Replace("/", "_").Replace(":", "_") + "/";
                }
            }
            else if (!string.IsNullOrEmpty(Program.g_AASEnv?.AssetAdministrationShells[0].Id))
            {
                _namespaceURI = "http://industrialdigitaltwin.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].Id.Replace("/", "_").Replace(":", "_") + "/";
            }
            else if (!string.IsNullOrEmpty(Program.g_AASEnv?.AssetAdministrationShells[0].Identification?.Id))
            {
                _namespaceURI = "http://industrialdigitaltwin.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].Identification.Id.Replace("/", "_").Replace(":", "_") + "/";
            }

            namespaceUris.Add(_namespaceURI);

            NamespaceUris = namespaceUris;
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            // for new nodes we create, pick our default namespace
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), (ushort)Server.NamespaceUris.GetIndex(_namespaceURI));
        }

        public void SaveNodestateCollectionAsNodeSet2(string filePath, NodeStateCollection nodesToExport)
        {
            if (nodesToExport.Count > 0)
            {
                using (var stream = new StreamWriter(filePath))
                {
                    UANodeSet nodeSet = new()
                    {
                        LastModified = DateTime.UtcNow,
                        LastModifiedSpecified = true
                    };

                    nodeSet.Models = [new ModelTableEntry() { ModelUri = _namespaceURI, Version = "0.5.0", PublicationDate = DateTime.UtcNow }];

                    // first add all nodes to the NodeSet
                    foreach (NodeState node in nodesToExport)
                    {
                        nodeSet.Export(SystemContext, node);
                    }

                    // now remove duplicates
                    List<UANode> nodes = nodeSet.Items.ToList();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        // remove all duplicate nodes from the List, based on node ID
                        for (int j = i + 1; j < nodes.Count; j++)
                        {
                            if (nodes[i].NodeId == nodes[j].NodeId)
                            {
                                nodes.RemoveAt(j);
                                j--;
                            }
                        }
                    }

                    // write the NodeSet to the file
                    nodeSet.Items = nodes.ToArray();
                    nodeSet.Write(stream.BaseStream);
                }
            }
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

                _rootAssetAdminShells = CreateFolder(FindNodeInAddressSpace(ObjectIds.ObjectsFolder), "Asset Admin Shells");
                _rootSubmodels = CreateFolder(FindNodeInAddressSpace(ObjectIds.ObjectsFolder), "Submodels");
                _rootConceptDescriptions = CreateFolder(FindNodeInAddressSpace(ObjectIds.ObjectsFolder), "Concept Descriptions");

                if (Program.g_AASEnv != null)
                {
                    CreateObjects(Program.g_AASEnv);

                    if (!Path.Exists(Path.Combine(Directory.GetCurrentDirectory(), "NodeSets")))
                    {
                        Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "NodeSets"));
                    }

                    string c_exportFilename;
                    if (Program.g_AASEnv.AssetAdministrationShells[0].IdShort != null)
                    {
                        string aasIdShort = Program.g_AASEnv.AssetAdministrationShells[0].IdShort.Replace("/", "_").Replace(":", "_");
                        if (string.IsNullOrEmpty(aasIdShort) || aasIdShort == "defaultAdminShell")
                        {
                            aasIdShort = Program.g_AASEnv.AssetAdministrationShells[0].Id.Replace("/", "_").Replace(":", "_");
                        }

                        c_exportFilename = Path.Combine(Directory.GetCurrentDirectory(), "NodeSets", aasIdShort + ".NodeSet2.xml");

                        try
                        {
                            NodeStateCollection nodesToExport = new();
                            foreach (NodeState node in PredefinedNodes.Values)
                            {
                                // only export nodes belonging to our AAS submodel-template namespace
                                if (node.NodeId.NamespaceIndex == (ushort)Server.NamespaceUris.GetIndex(_namespaceURI))
                                {
                                    nodesToExport.Add(node);
                                }
                            }

                            // export nodeset XML
                            Console.WriteLine($"Writing {nodesToExport.Count} nodes to file {c_exportFilename}!");
                            SaveNodestateCollectionAsNodeSet2(c_exportFilename, nodesToExport);

                            Console.WriteLine();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message + " when exporting to {0}", c_exportFilename);
                        }
                    }
                }

                AddReverseReferences(externalReferences);
                base.CreateAddressSpace(externalReferences);
            }
        }

        public void CreateObjects(AssetAdministrationShellEnvironment env)
        {
            if (env.AssetAdministrationShells != null)
            {
                foreach (AssetAdministrationShell aas in env.AssetAdministrationShells)
                {
                    // fall back to ID if no IDShort is provided
                    if (string.IsNullOrEmpty(aas.IdShort))
                    {
                        aas.IdShort = aas.Id;
                    }

                    // fall back to Identification if no IDShort and no ID is provided
                    if (string.IsNullOrEmpty(aas.IdShort))
                    {
                        aas.IdShort = aas.Identification.Id;
                    }

                    if (string.IsNullOrEmpty(aas.IdShort))
                    {
                        // skip this AAS
                        continue;
                    }

                    FolderState aasNode = CreateFolder(_rootAssetAdminShells, aas.IdShort);

                    if ((aas.AssetInformation != null) && !string.IsNullOrEmpty(aas.AssetInformation.GlobalAssetId))
                    {
                        CreateStringVariable(aasNode, aas.AssetInformation.GlobalAssetId, string.Empty);
                    }

                    if (aas.Submodels != null && aas.Submodels.Count > 0)
                    {
                        foreach (ModelReference reference in aas.Submodels)
                        {
                            CreateStringVariable(aasNode, reference.Keys[0].Value, string.Empty);
                        }
                    }
                }
            }

            if (env.Submodels != null)
            {
                foreach (Submodel submodel in env.Submodels)
                {
                    // fall back to ID if no IDShort is provided
                    if (string.IsNullOrEmpty(submodel.IdShort))
                    {
                        submodel.IdShort = submodel.Id;
                    }

                    // fall back to Identification if no IDShort and no ID is provided
                    if (string.IsNullOrEmpty(submodel.IdShort))
                    {
                        submodel.IdShort = submodel.Identification.Id;
                    }

                    if (string.IsNullOrEmpty(submodel.IdShort))
                    {
                        submodel.IdShort = submodel.Identification.Id;
                    }

                    if (string.IsNullOrEmpty(submodel.IdShort))
                    {
                        // skip this Submodel
                        continue;
                    }

                    FolderState submodelNode = CreateFolder(_rootSubmodels, submodel.IdShort);

                    if (submodel.SubmodelElements.Count > 0)
                    {
                        foreach (SubmodelElement sme in submodel.SubmodelElements)
                        {
                            CreateSubmodelElement(submodelNode, sme);
                        }
                    }
                }
            }

            if (env.ConceptDescriptions != null)
            {
                foreach (ConceptDescription cd in env.ConceptDescriptions)
                {
                    // fall back to ID if no IDShort is provided
                    if (string.IsNullOrEmpty(cd.IdShort))
                    {
                        cd.IdShort = cd.Id;
                    }

                    // fall back to Identification if no IDShort and no ID is provided
                    if (string.IsNullOrEmpty(cd.IdShort))
                    {
                        cd.IdShort = cd.Identification.Id;
                    }

                    if (string.IsNullOrEmpty(cd.IdShort))
                    {
                        cd.IdShort = cd.Identification.Id;
                    }

                    if (string.IsNullOrEmpty(cd.IdShort))
                    {
                        // skip this concept description
                        continue;
                    }

                    if (string.IsNullOrEmpty(cd.Id))
                    {
                        cd.Id = cd.IdShort;
                    }

                    CreateStringVariable(_rootConceptDescriptions, cd.Id, cd.IdShort);
                }
            }
        }

        private void CreateSubmodelElement(NodeState parent, SubmodelElement sme)
        {
            if (sme is SubmodelElementCollection collection)
            {
                if (collection.Value != null)
                {
                    if (string.IsNullOrEmpty(sme.IdShort) && (sme.SemanticId.Keys != null) && (sme.SemanticId.Keys.Count > 0))
                    {
                        sme.IdShort = sme.SemanticId.Keys[0].Value;
                    }

                    FolderState collectionFolder = CreateFolder(parent, sme.IdShort);

                    foreach (SubmodelElement smeChild in collection.Value)
                    {
                        CreateSubmodelElement(collectionFolder, smeChild);
                    }
                }
            }
            else
            {
                // fall back to SemanticID if no IDShort is provided
                if (string.IsNullOrEmpty(sme.IdShort) && (sme.SemanticId.Keys != null) && (sme.SemanticId.Keys.Count > 0))
                {
                    sme.IdShort = sme.SemanticId.Keys[0].Value;
                }

                if (sme is Property)
                {
                    CreateStringVariable(parent, sme.IdShort, ((Property)sme).Value);
                }
                else if (sme is Blob)
                {
                    CreateStringVariable(parent, sme.IdShort, ((Blob)sme).Value);
                }
                else if (sme is File)
                {
                    CreateStringVariable(parent, sme.IdShort, ((File)sme).Value);
                }
                else if (sme is ReferenceElement)
                {
                    if (string.IsNullOrEmpty(sme.IdShort) && (((ReferenceElement)sme).Value != null) && (((ReferenceElement)sme).Value.Keys.Count > 0) && (((ReferenceElement)sme).Value.Keys[0].Value != null))
                    {
                        sme.IdShort = ((ReferenceElement)sme).Value.Keys[0].Value;
                        CreateStringVariable(parent, sme.IdShort, string.Empty);
                    }
                }
                else
                {
                    // use the SemanticID as the value of the variable
                    string value = string.Empty;
                    if ((sme.SemanticId.Keys != null) && (sme.SemanticId.Keys.Count > 0))
                    {
                        value = sme.SemanticId.Keys[0].Value;
                    }

                    CreateStringVariable(parent, sme.IdShort, value);
                }
            }
        }

        public FolderState CreateFolder(NodeState? parent, string browseDisplayName)
        {
            if (string.IsNullOrEmpty(browseDisplayName))
            {
                throw new ArgumentNullException("Cannot create UA folder with empty browsename!");
            }

            FolderState folder = new(parent)
            {
                BrowseName = browseDisplayName,
                DisplayName = browseDisplayName,
                TypeDefinitionId = ObjectTypeIds.FolderType
            };

            folder.NodeId = New(SystemContext, folder);

            AddPredefinedNode(SystemContext, folder);

            if (parent != null)
            {
                parent.AddChild(folder);
            }

            return folder;
        }

        public BaseObjectState CreateObject(NodeState? parent, string idShort, string nodeId)
        {
            if (string.IsNullOrEmpty(idShort) || string.IsNullOrEmpty(nodeId))
            {
                throw new ArgumentNullException("Cannot create UA object with empty browsename or type definition!");
            }

            BaseObjectState obj = new(parent)
            {
                BrowseName = idShort,
                DisplayName = idShort,
                TypeDefinitionId = nodeId
            };

            obj.NodeId = New(SystemContext, obj);

            AddPredefinedNode(SystemContext, obj);

            if (parent != null)
            {
                parent.AddChild(obj);
            }

            return obj;
        }

        public BaseDataVariableState CreateStringVariable(NodeState? parent, string browseDisplayName, string value)
        {
            if (string.IsNullOrEmpty(browseDisplayName))
            {
                throw new ArgumentNullException("Cannot create UA variable with empty browsename!");
            }

            BaseDataVariableState variable = new(parent)
            {
                SymbolicName = browseDisplayName,
                BrowseName = new QualifiedName(browseDisplayName, (ushort)Server.NamespaceUris.GetIndex(_namespaceURI)),
                DisplayName = new Opc.Ua.LocalizedText("en", browseDisplayName),
                DataType = new NodeId(DataTypes.String),
                Value = value,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                UserWriteMask = AttributeWriteMask.ValueForVariableType,
                WriteMask = AttributeWriteMask.ValueForVariableType,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType
            };

            variable.NodeId = New(SystemContext, variable);

            AddPredefinedNode(SystemContext, variable);

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }
    }
}
