
namespace AdminShell
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using System.Collections.Generic;

    public class SimpleServer : StandardServer
    {
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            List<INodeManager> nodeManagers = new()
            {
                new I4AASNodeManager(server, configuration)
            };

            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties
            {
                ManufacturerName = "OPC Foundation",
                ProductName = "AAS2Nodeset",
                ProductUri = "",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };

            return properties;
        }
    }
}
