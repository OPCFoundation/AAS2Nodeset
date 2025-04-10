
using AdminShell;
using Newtonsoft.Json;
using Opc.Ua;
using Opc.Ua.Configuration;
using System.Xml;
using System.Xml.Serialization;

namespace AAS2Nodeset
{
    class Program
    {
        public static AssetAdministrationShellEnvironment? g_AASEnv;

        static void Main(string[] args)
        {
            // read XML AAS models
            Dictionary<string, string> xmlModels = RetrieveModelsFromDirectory("*.xml");

            Console.WriteLine();

            // parse XML AAS models
            foreach (KeyValuePair<string, string> xmlModel in xmlModels)
            {
                try
                {
                    Console.WriteLine("Processing " + xmlModel.Key + "...");

                    string nsURI = TryReadXmlFirstElementNamespaceURI(xmlModel.Value);

                    XmlReaderSettings settings = new XmlReaderSettings();
                    settings.ConformanceLevel = ConformanceLevel.Document;
                    XmlReader reader = XmlReader.Create(new StringReader(xmlModel.Value), settings);

                    // read V1.0
                    if (nsURI != null && nsURI.Trim() == "http://www.admin-shell.io/aas/1/0")
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(AssetAdministrationShellEnvironment), "http://www.admin-shell.io/aas/1/0");
                        g_AASEnv = serializer.Deserialize(reader) as AssetAdministrationShellEnvironment;
                    }

                    // read V2.0
                    if (nsURI != null && nsURI.Trim() == "http://www.admin-shell.io/aas/2/0")
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(AssetAdministrationShellEnvironment), "http://www.admin-shell.io/aas/2/0");
                        g_AASEnv = serializer.Deserialize(reader) as AssetAdministrationShellEnvironment;
                    }

                    // read V3.0
                    if (nsURI != null && nsURI.Trim() == "http://www.admin-shell.io/aas/3/0")
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(AssetAdministrationShellEnvironment), "http://www.admin-shell.io/aas/3/0");
                        g_AASEnv = serializer.Deserialize(reader) as AssetAdministrationShellEnvironment;
                    }

                    // read V3.1
                    if (nsURI != null && nsURI.Trim() == "https://admin-shell.io/aas/3/0")
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(AssetAdministrationShellEnvironment), "https://admin-shell.io/aas/3/0");
                        g_AASEnv = serializer.Deserialize(reader) as AssetAdministrationShellEnvironment;
                    }

                    reader.Close();

                    ExportNodeset();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}, skipping this model!");
                    Console.WriteLine();
                }
            }

            // read JSON AAS models
            Dictionary<string, string> jsonModels = RetrieveModelsFromDirectory("*.json");

            Console.WriteLine();

            // parse JSON AAS models
            foreach (KeyValuePair<string, string> jsonModel in jsonModels)
            {
                try
                {
                    Console.WriteLine("Processing " + jsonModel.Key + "...");

                    JsonSerializerSettings settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
                    g_AASEnv = JsonConvert.DeserializeObject<AssetAdministrationShellEnvironment>(jsonModel.Value, settings);

                    ExportNodeset();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}, skipping this model!");
                    Console.WriteLine();
                }
            }

            Console.WriteLine();
            Console.WriteLine("Done.");
        }

        private static void ExportNodeset()
        {
            if ((g_AASEnv != null) && (g_AASEnv.Submodels.Count > 0))
            {
                // convert to NodeSet2 by starting an OPC UA server, converting the AAS Environment according to I4AAS spec and exporting the server's address space
                ApplicationInstance app = new();
                ApplicationConfiguration config = app.LoadApplicationConfiguration(Path.Combine(Directory.GetCurrentDirectory(), "Application.Config.xml"), false).GetAwaiter().GetResult();

                app.CheckApplicationInstanceCertificate(false, 0);

                // create OPC UA cert validator
                app.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
                app.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidationCallback);
                app.ApplicationConfiguration.CertificateValidator.Update(app.ApplicationConfiguration.SecurityConfiguration).GetAwaiter().GetResult();

                app.Start(new SimpleServer());
                app.Stop();
            }
        }

        private static Dictionary<string, string> RetrieveModelsFromDirectory(string searchPattern)
        {
            Dictionary<string, string> models = new();

            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true
            };

            string? modelPath = Environment.GetEnvironmentVariable("MODEL_PATH");
            if (modelPath == null || !Path.IsPathFullyQualified(modelPath))
            {
                // use application directory instead
                modelPath = Directory.GetCurrentDirectory();
            }

            foreach (string filePath in Directory.EnumerateFiles(modelPath, searchPattern, options))
            {
                Console.WriteLine($"Found model: {filePath}");
                string modelDefinition = System.IO.File.ReadAllText(filePath);
                models.Add(filePath, modelDefinition);
            }

            return models;
        }

        private static string TryReadXmlFirstElementNamespaceURI(string model)
        {
            string nsURI = string.Empty;

            try
            {
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.ConformanceLevel = ConformanceLevel.Document;
                XmlReader reader = XmlReader.Create(new StringReader(model), settings);

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        nsURI = reader.NamespaceURI;
                        break;
                    }
                }

                reader.Close();
            }
            catch (Exception)
            {
                // do nothing
            }

            return nsURI;
        }

        private static void CertificateValidationCallback(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            // always trust the OPC UA certificate
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = true;
            }
        }
    }
}
