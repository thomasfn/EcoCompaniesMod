# Eco Companies
A server mod for Eco 9.4 that extends the law and economy system with player controllable companies.

## Installation
1. Download `EcoCompaniesMod.dll` from the [latest release](https://github.com/thomasfn/EcoCompaniesMod/releases).
2. Copy the `EcoCompaniesMod.dll` file to `Mods` folder of the dedicated server.
3. Restart the server.

## Usage

Companies are created and managed through a set of chat commands. All related commands are sub-commands of `/company`.

### Overview
A company can be created at any time by any player using `/company create <name>`.

### Managing Employees
A company has one CEO and zero to many employees. The CEO is implicitly considered an employee also. The CEO can invite other players to the company using `/company invite <playername>`, and they must accept the invitation to join using `/company join <companyname>`. A player can only be in one company at a time, though they can leave at any time using `/company leave`. The CEO can also fire employees using `/company fire <playername>`.

### Company Bank Account
Every company has a legal person and a bank account created for it. The name of the legal person will be `XXX Legal Person` and the name of the bank account will be `XXX Company Account`, where `XXX` is the name of the company. The legal person is a fake user of sorts and is seen by the citizen timer law trigger. All employees are given user rights to the company bank account but not manager rights, meaning that company wealth is not included as part of their wealth. The legal person is the sole manager of the account and considered to have 100% of the wealth of the company. The user list of the bank account is automatically updated whenever someone joins or leaves the company.

### Company Property
Every company can own property. This is achieved by a player passed ownership of a deed to the company's legal person. Like bank accounts, all employees will be automatically authed, but also invited as residents. Due to technical limitations of game, employees will not be able to directly edit the deed after doing this (e.g. claim more plots, unclaim existing plots or remove the whole deed). Instead they must stand on the plot and use `/company editplot`, or change ownership of the deed back to themself, make the edit and then pass it back to the legal person.

### Legislation
The following game values are added to assist with writing company-aware laws.

#### Account Legal Person
Retrieves the legal person user from a given bank account. This is helpful to derive the subject company, if any, for law triggers that involve a currency transaction, e.g. "Currency Transfer".

#### Employer Legal Person
Retrieves the legal person user from a given employee user. This is helpful to derive the employer company, if any, for law triggers that involve a citizen - for example, placing blocks, cutting trees or claiming property.

#### Employee Count
Retrieves the number of employees of a company, including the CEO. The legal person for the company will be needed as context.

#### Skill Count
Retrieves the number of specialisations of all employees of a company, including Self Improvement. There is an option to choose unique skills only or not. The legal person for the company will be needed as context.

#### Is CEO Of Company
Gets if the given citizen is the CEO of any company.

#### Is Employee Of Company
Gets if the given citizen is the employee of any company.

#### Is Company Legal Person
Gets if the given citizen is the generated legal person user for a company.

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