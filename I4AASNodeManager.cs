
namespace AdminShell
{
    using AAS2Nodeset;
    using Newtonsoft.Json;
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

        private const string c_dppNamespaceURI = "http://opcfoundation.org/UA/DigitalProductPassport/";

        private const string c_dppNodesetFilename = "Opc.Ua.DigitalProductPassport.NodeSet2.xml";

        // node IDs (numeric identifiers) of the types defined in the DPP companion specification
        private const uint c_singleValuedDataElementType = 12;
        private const uint c_multiValuedDataElementType = 15;
        private const uint c_dataElementCollectionType = 19;
        private const uint c_relatedResourceType = 22;
        private const uint c_multiLanguageDataElementType = 27;
        private const uint c_digitalProductPassportType = 29;
        private const uint c_granularityEnumeration = 124;

        public BaseObjectState? _rootSubmodels = null;

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
                    _namespaceURI = "http://industrialdigitaltwin.org/UA/" + Program.g_AASEnv?.AssetAdministrationShells[0].IdShort?.Replace("/", "_").Replace(":", "_") + "/";
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
            namespaceUris.Add(c_dppNamespaceURI);

            NamespaceUris = namespaceUris;
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            // for new nodes we create, pick our default namespace
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), (ushort)Server.NamespaceUris.GetIndex(_namespaceURI));
        }

        private ushort DppNamespaceIndex => (ushort)Server.NamespaceUris.GetIndex(c_dppNamespaceURI);

        private NodeId DppNodeId(uint id) => new NodeId(id, DppNamespaceIndex);

        private void LoadDppNodeset()
        {
            string nodesetPath = Path.Combine(AppContext.BaseDirectory, c_dppNodesetFilename);
            if (!System.IO.File.Exists(nodesetPath))
            {
                nodesetPath = Path.Combine(Directory.GetCurrentDirectory(), c_dppNodesetFilename);
            }

            using (FileStream stream = new(nodesetPath, FileMode.Open, FileAccess.Read))
            {
                UANodeSet nodeSet = UANodeSet.Read(stream);

                NodeStateCollection predefinedNodes = new();
                nodeSet.Import(SystemContext, predefinedNodes);

                foreach (NodeState node in predefinedNodes)
                {
                    AddPredefinedNode(SystemContext, node);
                }
            }
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

                    nodeSet.Models = [new ModelTableEntry() {
                        ModelUri = _namespaceURI, Version = "1.0.0",
                        PublicationDateSpecified = true,
                        PublicationDate = DateTime.UtcNow.Date,
                        RequiredModel = [new ModelTableEntry() { ModelUri = c_dppNamespaceURI, Version = "1.0.0" }] }];

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

        private void SaveValues(string filePath, NodeStateCollection nodesToExport)
        {
            if (nodesToExport.Count > 0)
            {
                // first remove duplicates
                List<NodeState> nodes = nodesToExport.ToList();
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

                // capture name-value pairs of all variables in the NodeSet
                Dictionary<string, string> values = new();
                foreach (NodeState node in nodes)
                {
                    if (node is BaseVariableState variable)
                    {
                        string value = variable.Value is Array array
                            ? string.Join(", ", array.Cast<object>().Select(v => v?.ToString()))
                            : variable.Value?.ToString() ?? string.Empty;

                        values.Add(variable.NodeId.ToString().Replace("ns=2", "ns=1"), value);
                    }
                }

                System.IO.File.WriteAllText(filePath.Replace(".NodeSet2.xml", ".values.json"), JsonConvert.SerializeObject(values, Formatting.Indented));
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

                LoadDppNodeset();

                _rootSubmodels = CreateObject(null, "DigitalProductPassport", DppNodeId(c_digitalProductPassportType));
                _rootSubmodels.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                objectsFolderReferences.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, _rootSubmodels.NodeId));

                if (Program.g_AASEnv != null)
                {
                    CreateObjects(Program.g_AASEnv);

                    if (!Path.Exists(Path.Combine(Directory.GetCurrentDirectory(), "NodeSets")))
                    {
                        Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "NodeSets"));
                    }

                    string c_exportFilename;
                    if (Program.g_AASEnv.AssetAdministrationShells[0].IdShort is string rawIdShort)
                    {
                        string aasIdShort = rawIdShort.Replace("/", "_").Replace(":", "_");
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
                            SaveValues(c_exportFilename, nodesToExport);

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
            // create the properties of the DigitalProductPassportType
            string dppId = env.AssetAdministrationShells?.FirstOrDefault()?.Id ?? string.Empty;
            CreateProperty(_rootSubmodels, "DigitalProductPassportId", DataTypeIds.String, dppId);
            CreateProperty(_rootSubmodels, "UniqueProductIdentifier", DataTypeIds.String, string.Empty);
            CreateProperty(_rootSubmodels, "Granularity", DppNodeId(c_granularityEnumeration), 0);
            CreateProperty(_rootSubmodels, "DppSchemaVersion", DataTypeIds.String, string.Empty);
            CreateProperty(_rootSubmodels, "DppStatus", DataTypeIds.String, string.Empty);
            CreateProperty(_rootSubmodels, "LastUpdated", DataTypeIds.DateTime, DateTime.MinValue);
            CreateProperty(_rootSubmodels, "EconomicOperatorId", DataTypeIds.String, string.Empty);
            CreateProperty(_rootSubmodels, "FacilityId", DataTypeIds.String, string.Empty);

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
                        // skip this Submodel
                        continue;
                    }

                    BaseObjectState submodelNode = CreateDataElementObject(_rootSubmodels, submodel.IdShort, c_dataElementCollectionType, submodel.SemanticId);

                    if (submodel.SubmodelElements.Count > 0)
                    {
                        foreach (SubmodelElement sme in submodel.SubmodelElements)
                        {
                            CreateDataElement(submodelNode, sme);
                        }
                    }
                }
            }
        }

        private void CreateDataElement(NodeState parent, SubmodelElement sme)
        {
            if (sme is SubmodelElementCollection collection)
            {
                if (collection.Value != null)
                {
                    if (string.IsNullOrEmpty(sme.IdShort) && (sme.SemanticId.Keys != null) && (sme.SemanticId.Keys.Count > 0))
                    {
                        sme.IdShort = sme.SemanticId.Keys[0].Value;
                    }

                    BaseObjectState collectionFolder = CreateDataElementObject(parent, sme.IdShort, c_dataElementCollectionType, sme.SemanticId);

                    foreach (SubmodelElement smeChild in collection.Value)
                    {
                        CreateDataElement(collectionFolder, smeChild);
                    }
                }
            }
            else if (sme is SubmodelElementList list)
            {
                if (list.Value != null)
                {
                    if (string.IsNullOrEmpty(sme.IdShort) && (sme.SemanticId.Keys != null) && (sme.SemanticId.Keys.Count > 0))
                    {
                        sme.IdShort = sme.SemanticId.Keys[0].Value;
                    }

                    BaseObjectState listFolder = CreateDataElementObject(parent, sme.IdShort, c_multiValuedDataElementType, sme.SemanticId);

                    foreach (SubmodelElement smeChild in list.Value)
                    {
                        CreateDataElement(listFolder, smeChild);
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

                if (sme is Property property)
                {
                    BaseObjectState element = CreateDataElementObject(parent, sme.IdShort, c_singleValuedDataElementType, sme.SemanticId);
                    CreateProperty(element, "Value", DataTypeIds.String, property.Value ?? string.Empty);
                }
                else if (sme is MultiLanguageProperty multiLanguageProperty)
                {
                    BaseObjectState element = CreateDataElementObject(parent, sme.IdShort, c_multiLanguageDataElementType, sme.SemanticId);

                    Opc.Ua.LocalizedText[] texts = (multiLanguageProperty.Value ?? new()).Select(entry => new Opc.Ua.LocalizedText(entry.Language, entry.Text)).ToArray();
                    CreateProperty(element, "Value", DataTypeIds.LocalizedText, texts, ValueRanks.OneDimension);
                }
                else if (sme is File file)
                {
                    BaseObjectState element = CreateDataElementObject(parent, sme.IdShort, c_relatedResourceType, sme.SemanticId);

                    CreateProperty(element, "ContentType", DataTypeIds.String, file.MimeType ?? string.Empty);
                    CreateProperty(element, "Url", new NodeId(23751), file.Value ?? string.Empty);
                }
                else
                {
                    // for other types of SubmodelElements, there is no mapping to the DPP
                    Console.WriteLine($"Warning: SubmodelElement of type {sme.GetType().Name} with idShort '{sme.IdShort}' has no mapping to the DPP and will be created as an empty SingleValuedDataElement in the OPC UA address space.");

                    BaseObjectState element = CreateDataElementObject(parent, sme.IdShort, c_singleValuedDataElementType, sme.SemanticId);
                    CreateProperty(element, "Value", DataTypeIds.String, string.Empty);
                }
            }
        }

        private BaseObjectState CreateDataElementObject(NodeState? parent, string? idShort, uint dppTypeId, Reference? semanticId)
        {
            BaseObjectState element = CreateObject(parent, idShort ?? string.Empty, DppNodeId(dppTypeId));

            CreateProperty(element, "ElementId", DataTypeIds.String, idShort ?? string.Empty);

            if ((semanticId?.Keys != null) && (semanticId.Keys.Count > 0))
            {
                CreateProperty(element, "DictionaryReference", DataTypeIds.String, semanticId.Keys[0].Value ?? string.Empty);
            }

            return element;
        }

        public BaseObjectState CreateObject(NodeState? parent, string browseDisplayName, NodeId typeDefinitionId)
        {
            if (string.IsNullOrEmpty(browseDisplayName))
            {
                browseDisplayName = "DataElement";
            }

            BaseObjectState obj = new(parent)
            {
                SymbolicName = browseDisplayName,
                BrowseName = new QualifiedName(browseDisplayName, (ushort)Server.NamespaceUris.GetIndex(_namespaceURI)),
                DisplayName = new Opc.Ua.LocalizedText("en", browseDisplayName),
                TypeDefinitionId = typeDefinitionId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };

            obj.NodeId = New(SystemContext, obj);

            AddPredefinedNode(SystemContext, obj);

            if (parent != null)
            {
                parent.AddChild(obj);
            }

            return obj;
        }

        public PropertyState CreateProperty(NodeState? parent, string browseDisplayName, NodeId dataType, object value, int valueRank = ValueRanks.Scalar)
        {
            PropertyState property = new(parent)
            {
                SymbolicName = browseDisplayName,
                BrowseName = new QualifiedName(browseDisplayName, DppNamespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", browseDisplayName),
                DataType = dataType,
                Value = value,
                ValueRank = valueRank,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                UserWriteMask = AttributeWriteMask.ValueForVariableType,
                WriteMask = AttributeWriteMask.ValueForVariableType,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty
            };

            if (valueRank == ValueRanks.OneDimension)
            {
                property.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            }

            property.NodeId = New(SystemContext, property);

            AddPredefinedNode(SystemContext, property);

            if (parent != null)
            {
                parent.AddChild(property);
            }

            return property;
        }
    }
}
