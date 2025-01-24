
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
                _namespaceURI = "http://opcfoundation.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].IdShort.Replace("/", "_").Replace(":", "_") + "/";
            }
            else if (!string.IsNullOrEmpty(Program.g_AASEnv?.AssetAdministrationShells[0].Id))
            {
                _namespaceURI = "http://opcfoundation.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].Id.Replace("/", "_").Replace(":", "_") + "/";
            }
            else if (!string.IsNullOrEmpty(Program.g_AASEnv?.AssetAdministrationShells[0].Identification?.Id))
            {
                _namespaceURI = "http://opcfoundation.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].Identification.Id.Replace("/", "_").Replace(":", "_") + "/";
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

                    foreach (NodeState node in nodesToExport)
                    {
                        // make sure we don't add duplicates
                        bool alreadyExists = false;
                        if (nodeSet.Items != null)
                        {
                            foreach (UANode existingNode in nodeSet.Items)
                            {
                                if (existingNode.NodeId == node.NodeId.ToString().Replace("ns=2", "ns=1"))
                                {
                                    alreadyExists = true;
                                    break;
                                }
                            }
                        }

                        if (!alreadyExists)
                        {
                            nodeSet.Export(SystemContext, node);
                        }
                    }

                    nodeSet.Write(stream.BaseStream);
                    stream.Flush();
                }

                // fixup our model definitions
                string exportedContent = System.IO.File.ReadAllText(filePath);
                exportedContent = exportedContent.Replace("</NamespaceUris>", "</NamespaceUris>\n" +
                    "  <Models>\n" +
                    "    <Model ModelUri=\"" + _namespaceURI + "\" Version=\"1.0.0\" PublicationDate=\"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\"/>\n" +
                    "  </Models>");
                System.IO.File.WriteAllText(filePath, exportedContent);
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
                        c_exportFilename = Path.Combine(Directory.GetCurrentDirectory(), "NodeSets", Program.g_AASEnv.AssetAdministrationShells[0].IdShort.Replace("/", "_").Replace(":", "_") + ".NodeSet2.xml");

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

                    FolderState aasNode = CreateFolder(_rootAssetAdminShells, aas.IdShort);

                    if ((aas.AssetInformation != null) && !string.IsNullOrEmpty(aas.AssetInformation.GlobalAssetId))
                    {
                        CreateStringVariable(aasNode, aas.AssetInformation.GlobalAssetId, string.Empty);
                    }

                    if (aas.Submodels != null && aas.Submodels.Count > 0)
                    {
                        foreach (SubmodelReference reference in aas.Submodels)
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
                    FolderState submodelNode = CreateFolder(_rootSubmodels, submodel.IdShort);

                    if (submodel.SubmodelElements.Count > 0)
                    {
                        foreach (SubmodelElementWrapper smew in submodel.SubmodelElements)
                        {
                            CreateSubmodelElement(submodelNode, smew.SubmodelElement);
                        }
                    }
                }
            }

            if (env.ConceptDescriptions != null)
            {
                foreach (ConceptDescription cd in env.ConceptDescriptions)
                {
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

                    foreach (SubmodelElementWrapper smew in collection.Value)
                    {
                        CreateSubmodelElement(collectionFolder, smew.SubmodelElement);
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
