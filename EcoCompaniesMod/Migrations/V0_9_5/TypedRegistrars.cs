namespace Eco.Mods.Companies.Migrations.V0_9_5
{
    using Eco.Core.Serialization.Migrations;
    using Eco.Core.Serialization.Migrations.Attributes;
    using Eco.Core.Systems;

    [Migration(SinceVersion = 3.915f)]
    [MigrationType(typeof(Registrar<Company>))]
    public class TypedRegistrarMigration : AggregateMigration
    {
        public TypedRegistrarMigration()
        {
            this.MigrateRegistrars();
        }

        private void MigrateRegistrars()
        {
            this.AddDataMigration("class[Eco.Mods.Companies.CompaniesData]", dm =>
            {
                this.MigrateRegistrar(dm, "Companies", "class[Eco.Mods.Companies.Company]");
            });
        }

        private void MigrateRegistrar(DataMigration dataMigration, string memberName, string registarEntrySchemaType)
        {
            var memberMigration = new DataMigration("Eco.Core.Systems.Registrar");
            memberMigration.ChangeMemberSchemaType("IdToObj", SchemaUtils.MakeDictionarySchemaType(SchemaUtils.GetSchemaType(typeof(int)), registarEntrySchemaType));
            memberMigration.ChangeSchemaType(SchemaUtils.MakeGenericClassSchemaType("Eco.Core.Systems.Registrar", registarEntrySchemaType));
            dataMigration.MigrateMember(memberName, memberMigration, skipIfMissing: true);
        }
    }
}