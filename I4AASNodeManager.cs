
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
        // AAS type nodeId constants from I4AAS Companion Spec
        const int c_assetAdministrationShellTypeNodeId = 1002;
        const int c_administrativeInformationTypeNodeId = 1030;
        const int c_qualifierTypeNodeId = 1032;
        const int c_identifierTypeNodeId = 1029;
        const int c_referenceTypeNodeId = 1004;
        const int c_dataSpecificationTypeNodeId = 1027;
        const int c_submodelElementTypeNodeId = 1009;
        const int c_submodelTypeNodeId = 1006;
        const int c_conceptDescriptionTypeNodeId = 1007;
        const int c_assetTypeNodeId = 1005;

        private long _lastUsedId = 0;

        private string _namespaceURI = "http://opcfoundation.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].IdShort + "/";

        public NodeState? _rootAssetAdminShells = null;
        public NodeState? _rootSubmodels = null;
        public NodeState? _rootConceptDescriptions = null;

        public I4AASNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            List<string> namespaceUris = new()
            {
                _namespaceURI
            };

            LoadNamespaceUrisFromNodesetXml(namespaceUris, "I4AAS.NodeSet2.xml");

            NamespaceUris = namespaceUris;
        }

        private void LoadNamespaceUrisFromNodesetXml(List<string> namespaceUris, string nodesetFile)
        {
            using (FileStream stream = new(nodesetFile, FileMode.Open, FileAccess.Read))
            {
                UANodeSet nodeSet = UANodeSet.Read(stream);

                if ((nodeSet.NamespaceUris != null) && (nodeSet.NamespaceUris.Length > 0))
                {
                    foreach (string ns in nodeSet.NamespaceUris)
                    {
                        if (!namespaceUris.Contains(ns))
                        {
                            namespaceUris.Add(ns);
                        }
                    }
                }
            }
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
                        nodeSet.Export(SystemContext, node);
                    }

                    nodeSet.Write(stream.BaseStream);
                    stream.Flush();
                }

                // fixup our model definitions
                string exportedContent = System.IO.File.ReadAllText(filePath);
                exportedContent = exportedContent.Replace("</NamespaceUris>", "</NamespaceUris>\n" +
                    "  <Models>\n" +
                    "    <Model ModelUri=\"" + _namespaceURI + "\" Version=\"1.0.0\" PublicationDate=\"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\">\n" +
                    "      <RequiredModel ModelUri=\"http://opcfoundation.org/UA/I4AAS/\" Version=\"5.0.0\" PublicationDate=\"2021-06-04T00:00:00Z\"/>\n" +
//                    "      <RequiredModel ModelUri=\"http://opcfoundation.org/UA/\" Version=\"1.04.3\" PublicationDate=\"2019-09-09T00:00:00Z\" />\n" +
                    "    </Model>\n" +
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

                AddNodesFromNodesetXml("I4AAS.NodeSet2.xml");

                _rootAssetAdminShells = CreateFolder(null, "Asset Admin Shells");
                objectsFolderReferences.Add(new NodeStateReference(ReferenceTypes.Organizes, false, _rootAssetAdminShells.NodeId));

                _rootSubmodels = CreateFolder(null, "Submodels");
                objectsFolderReferences.Add(new NodeStateReference(ReferenceTypes.Organizes, false, _rootSubmodels.NodeId));

                _rootConceptDescriptions = CreateFolder(null, "Concept Descriptions");
                objectsFolderReferences.Add(new NodeStateReference(ReferenceTypes.Organizes, false, _rootConceptDescriptions.NodeId));

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
                        c_exportFilename = Path.Combine(Directory.GetCurrentDirectory(), "NodeSets", Program.g_AASEnv.AssetAdministrationShells[0].IdShort + ".NodeSet2.xml");

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
            string typeNodeId = "ns=" + Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/I4AAS/").ToString() + ";i=";

            if (env.AssetAdministrationShells != null)
            {
                foreach (AssetAdministrationShell aas in env.AssetAdministrationShells)
                {
                    BaseObjectState aasNode = CreateObject(_rootAssetAdminShells, aas.IdShort, typeNodeId + c_assetAdministrationShellTypeNodeId.ToString());

                    if (aas.AssetInformation != null)
                    {
                        CreateObject(aasNode, aas.AssetInformation.ToString(), typeNodeId + c_assetTypeNodeId.ToString());
                    }

                    if (aas.Submodels != null && aas.Submodels.Count > 0)
                    {
                        foreach (SubmodelReference reference in aas.Submodels)
                        {
                            CreateVariable<string>(aasNode, "Submodel Reference", typeNodeId + c_referenceTypeNodeId.ToString(), reference.Keys[0].Value);
                        }
                    }
                }
            }

            if (env.Submodels != null)
            {
                foreach (Submodel submodel in env.Submodels)
                {
                    BaseObjectState submodelNode = CreateObject(_rootSubmodels, submodel.IdShort, typeNodeId + c_submodelTypeNodeId.ToString());

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
                    CreateObject(_rootConceptDescriptions, cd.IdShort, typeNodeId + c_conceptDescriptionTypeNodeId.ToString());
                }
            }

        }

        private void CreateSubmodelElement(NodeState parent, SubmodelElement sme)
        {
            string typeNodeId = "ns=" + Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/I4AAS/").ToString() + ";i=";

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
                    CreateVariable<string>(parent, sme.IdShort, typeNodeId + c_submodelElementTypeNodeId.ToString(), ((Property)sme).Value);
                }
                else if (sme is Blob)
                {
                    CreateVariable<string>(parent, sme.IdShort, typeNodeId + c_submodelElementTypeNodeId.ToString(), ((Blob)sme).Value);
                }
                else if (sme is File)
                {
                    CreateVariable<string>(parent, sme.IdShort, typeNodeId + c_submodelElementTypeNodeId.ToString(), ((File)sme).Value);
                }
                else
                {
                    CreateVariable<string>(parent, sme.IdShort, typeNodeId + c_submodelElementTypeNodeId.ToString(), string.Empty);
                }
            }
        }

        public FolderState CreateFolder(NodeState? parent, string browseDisplayName)
        {
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

        public BaseObjectState CreateObject(NodeState? parent, string? idShort, string nodeId)
        {
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

        public BaseDataVariableState<T> CreateVariable<T>(NodeState? parent, string? browseDisplayName, NodeId dataTypeId, T value)
        {
            BaseDataVariableState<T> variable = new(parent)
            {
                BrowseName = browseDisplayName,
                DisplayName = browseDisplayName,
                Description = new Opc.Ua.LocalizedText("en", browseDisplayName),
                DataType = dataTypeId,
                Value = (T)value,
                TypeDefinitionId = dataTypeId
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
