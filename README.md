# AAS2Nodeset
Tool converting from Catena-X/Tractus-X AAS XML/JSON to OPC UA Nodeset2.XML files for use as templates in Digital Product Passports (DPPs). Runs natively on Linux and Windows or in a Docker container.

## Configuration Environment Variables
* MODEL_PATH - Fully qualified path to models as input. The tool will use the application directory if not specified. Converted Nodesets will always be placed in a folder called "Nodesets" in the application directory.
