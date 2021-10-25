# Eco Companies
A server mod for Eco 9.4 that extends the law and economy system with a company system.

## Installation
1. Download `EcoCompaniesMod.dll` from the [latest release](https://github.com/thomasfn/EcoCompaniesMod/releases).
2. Copy the `EcoCompaniesMod.dll` file to `Mods` folder of the dedicated server.
3. Restart the server.

## Usage

TODO: Explain how to use the companies from a user and a legislator's perspective

## Building Mod from Source

### Windows

1. Login to the [Eco Website](https://play.eco/) and download the latest modkit
2. Extract the modkit and copy the dlls from `ReferenceAssemblies` to `eco-dlls` in the root directory (create the folder if it doesn't exist)
3. Open `EcoCompaniesMod.sln` in Visual Studio 2019
4. Build the `EcoCompaniesMod` project in Visual Studio
5. Find the artifact in `EcoCompaniesMod\bin\{Debug|Release}\net5.0`

### Linux

1. Run `ECO_BRANCH="release" MODKIT_VERSION="0.9.4.3-beta" fetch-eco-reference-assemblies.sh` (change the modkit branch and version as needed)
2. Enter the `EcoCompaniesMod` directory and run:
`dotnet restore`
`dotnet build`
3. Find the artifact in `EcoCompaniesMod/bin/{Debug|Release}/net5.0`

## License
[MIT](https://choosealicense.com/licenses/mit/)