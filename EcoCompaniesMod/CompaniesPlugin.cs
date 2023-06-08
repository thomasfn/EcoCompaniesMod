using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Collections.Generic;

using HarmonyLib;

namespace Eco.Mods.Companies
{
    using Core.Plugins.Interfaces;
    using Core.Utils;
    using Core.Systems;
    using Core.Serialization;
    using Core.Plugins;
    using Core.Controller;

    using Shared.Localization;
    using Shared.Utils;
    using Shared.Serialization;
    using Shared.Networking;
    using Shared.IoC;

    using Gameplay.Players;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Civics.GameValues;
    using Gameplay.Civics.Laws;
    using Gameplay.Aliases;
    using Gameplay.Items;
    using Gameplay.GameActions;
    using Gameplay.Utils;

    using Simulation.Time;

    [Localized]
    public class CompaniesConfig
    {
        [LocDescription("If enabled, employees may not have homestead deeds, and the company gets a HQ homestead deed that grows based on employee count.")]
        public bool PropertyLimitsEnabled { get; set; } = true;
    }

    [Serialized]
    public class CompaniesData : Singleton<CompaniesData>, IStorage
    {
        public IPersistent StorageHandle { get; set; }

        [Serialized] public Registrar<Company> Companies = new ();

        public readonly PeriodicUpdateConfig UpdateTimer = new PeriodicUpdateConfig(true);

        public void InitializeRegistrars()
        {
            this.Companies.PreInit(Localizer.DoStr("Companies"), true, CompaniesPlugin.Obj, Localizer.DoStr("Companies"));
        }

        public void Initialize()
        {
            
        }
    }

    [Eco]
    internal class CompanyLawManager : ILawManager, IController, IHasClientControlledContainers
    {
        private readonly LawManager internalLawManager;

        public CompanyLawManager(LawManager internalLawManager)
        {
            this.internalLawManager = internalLawManager;
        }

        public PostResult Perform(GameAction action)
        {
            var result = internalLawManager.Perform(action);
            if (action is PlaceOrPickUpObject placeOrPickUpObject)
            {
                CompanyManager.Obj.InterceptPlaceOrPickupObjectGameAction(placeOrPickUpObject, ref result);
            }
            return result;
        }

        #region IController
        public ref int ControllerID => ref internalLawManager.ControllerID;
        #endregion
    }

    [Localized, LocDisplayName(nameof(CompaniesPlugin)), Priority(PriorityAttribute.High)]
    public class CompaniesPlugin : Singleton<CompaniesPlugin>, IModKitPlugin, IConfigurablePlugin, IInitializablePlugin, ISaveablePlugin, IContainsRegistrars
    {
        public IPluginConfig PluginConfig => config;

        private PluginConfig<CompaniesConfig> config;
        public CompaniesConfig Config => config.Config;

        private static readonly Dictionary<Type, GameValueType> gameValueTypeCache = new Dictionary<Type, GameValueType>();

        public readonly CompanyManager CompanyManager;

        [NotNull] private readonly CompaniesData data;

        static CompaniesPlugin()
        {
            CosturaUtility.Initialize();
            Harmony.DEBUG = true;
            var harmony = new Harmony("Eco.Mods.Companies");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public CompaniesPlugin()
        {
            config = new PluginConfig<CompaniesConfig>("Companies");
            data = StorageManager.LoadOrCreate<CompaniesData>("Companies");
            CompanyManager = new CompanyManager();
        }

        public object GetEditObject() => Config;
        public ThreadSafeAction<object, string> ParamChanged { get; set; } = new ThreadSafeAction<object, string>();

        public void OnEditObjectChanged(object o, string param)
        {
            this.SaveConfig();
        }

        public void Initialize(TimedTask timer)
        {
            data.Initialize();
            InstallLawManagerHack();
            InstallGameValueHack();
        }

        private void InstallLawManagerHack()
        {
            var oldLawManager = ServiceHolder<ILawManager>.Obj;
            if (oldLawManager is LawManager oldLawManagerConcrete)
            {
                ServiceHolder<ILawManager>.Obj = new CompanyLawManager(oldLawManagerConcrete);
            }
            else
            {
                Logger.Error($"Failed to install law manager hack: ServiceHolder<ILawManager>.Obj was not of expected type");
            }
        }

        private void InstallGameValueHack()
        {
            var attr = typeof(GameValue).GetCustomAttribute<CustomRPCSetterAttribute>();
            attr.ContainerType = GetType();
            attr.MethodName = nameof(DynamicSetGameValue);
            var idToMethod = typeof(RPCManager).GetField("IdToMethod", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as Dictionary<int, RPCMethod>;
            if (idToMethod == null)
            {
                Logger.Error($"Failed to install game value hack: Couldn't retrieve RPCManager.IdToMethod");
            }
            var rpcMethodFuncProperty = typeof(RPCMethod).GetProperty(nameof(RPCMethod.Func), BindingFlags.Public | BindingFlags.Instance);
            if (rpcMethodFuncProperty == null)
            {
                Logger.Error($"Failed to install game value hack: Couldn't find RPCMethod.Func property");
                return;
            }
            var backingField = GetBackingField(rpcMethodFuncProperty);
            if (backingField == null)
            {
                Logger.Error($"Failed to install game value hack: Couldn't find RPCMethod.Func backing field");
                return;
            }
            var relevantRpcMethods = idToMethod.Values
                .Where(x => x.IsCustomSetter && x.PropertyInfo != null)
                .Where(x => x.PropertyInfo.PropertyType.IsAssignableTo(typeof(GameValue<IAlias>)) || x.PropertyInfo.PropertyType.IsAssignableTo(typeof(GameValue<User>)));
            foreach (var rpcMethod in relevantRpcMethods)
            {
                Func<object, object[], object> overrideFunc = (target, args) => { DynamicSetGameValue(target, rpcMethod.PropertyInfo, args[0]); return null; };
                backingField.SetValue(rpcMethod, overrideFunc);
            }
        }

        private static FieldInfo GetBackingField(PropertyInfo pi)
        {
            if (!pi.CanRead || !pi.GetGetMethod(nonPublic: true).IsDefined(typeof(CompilerGeneratedAttribute), inherit: true))
                return null;
            var backingField = pi.DeclaringType.GetField($"<{pi.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (backingField == null)
                return null;
            if (!backingField.IsDefined(typeof(CompilerGeneratedAttribute), inherit: true))
                return null;
            return backingField;
        }

        public static void DynamicSetGameValue(object parent, PropertyInfo prop, object newValue)
        {
            if (newValue is BSONObject obj) { newValue = BsonManipulator.FromBson(obj, typeof(IController)); }
            if (newValue is GameValueType gvt)
            {
                if (gvt.Type == typeof(AccountLegalPerson) && prop.PropertyType == typeof(GameValue<IAlias>))
                {
                    // The property wants an IAlias and the client sent an AccountLegalPerson, so remap it to AccountLegalPersonAlias
                    newValue = GetGameValueType<AccountLegalPersonAlias>();
                }
                else if (gvt.Type == typeof(AccountLegalPersonAlias) && prop.PropertyType == typeof(GameValue<User>))
                {
                    // The property wants a User and the client sent an AccountLegalPersonAlias, so remap it to AccountLegalPerson
                    // This shouldn't really be possible as AccountLegalPersonAlias is marked as NonSelectable but do it anyway to be safe
                    newValue = GetGameValueType<AccountLegalPerson>();
                }
                else if (gvt.Type == typeof(EmployerLegalPerson) && prop.PropertyType == typeof(GameValue<IAlias>))
                {
                    // The property wants an IAlias and the client sent an EmployerLegalPerson, so remap it to EmployerLegalPersonAlias
                    newValue = GetGameValueType<EmployerLegalPersonAlias>();
                }
                else if (gvt.Type == typeof(EmployerLegalPersonAlias) && prop.PropertyType == typeof(GameValue<User>))
                {
                    // The property wants a User and the client sent an EmployerLegalPersonAlias, so remap it to EmployerLegalPerson
                    // This shouldn't really be possible as EmployerLegalPersonAlias is marked as NonSelectable but do it anyway to be safe
                    newValue = GetGameValueType<EmployerLegalPerson>();
                }
                else if (gvt.Type == typeof(CompanyCeo) && prop.PropertyType == typeof(GameValue<IAlias>))
                {
                    // The property wants an IAlias and the client sent an CompanyCeo, so remap it to CompanyCeoAlias
                    newValue = GetGameValueType<CompanyCeoAlias>();
                }
                else if (gvt.Type == typeof(CompanyCeoAlias) && prop.PropertyType == typeof(GameValue<User>))
                {
                    // The property wants a User and the client sent an CompanyCeoAlias, so remap it to CompanyCeo
                    // This shouldn't really be possible as CompanyCeoAlias is marked as NonSelectable but do it anyway to be safe
                    newValue = GetGameValueType<CompanyCeo>();
                }
            }
            GameValueManager.DynamicSetGameValue(parent, prop, newValue);
        }

        private static GameValueType GetGameValueType<T>() where T : GameValue
            => gameValueTypeCache.GetOrAdd(typeof(T), () => new GameValueType()
                {
                    Type = typeof(T),
                    ChoosesType = typeof(T).GetStaticPropertyValue<Type>("ChoosesType"), // note: ignores Derived attribute
                    ContextRequirements = typeof(T).Attribute<RequiredContextAttribute>()?.RequiredTypes,
                    Name = typeof(T).Name,
                    Description = typeof(T).GetLocDescription(),
                    Category = typeof(T).Attribute<LocCategoryAttribute>()?.Category,
                    MarkedUpName = typeof(T).UILink(),
                });

        public void InitializeRegistrars(TimedTask timer) => data.InitializeRegistrars();
        public string GetDisplayText() => string.Empty;
        public string GetCategory() => Localizer.DoStr("Civics");
        public string GetStatus() => string.Empty;
        public override string ToString() => Localizer.DoStr("Companies");
        public void SaveAll() => StorageManager.Obj.MarkDirty(data);
    }
}