# AAS2Nodeset
Tool converting from AAS XML/JSON to OPC UA Nodeset2.XML files for Digital Product Passports (DPPs). Runs natively on Linux and Windows or in a Docker container.

## Configuration Environment Variables
* MODEL_PATH - Fully qualified path to AAS Models as Input. Tool will use application directory if not specified. Converted Nodesets will always be placed in a folder called "Nodesets" in the application directory.
