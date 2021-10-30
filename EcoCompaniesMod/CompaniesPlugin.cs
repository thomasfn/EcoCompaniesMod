using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Collections.Generic;

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

    using Gameplay.Players;
    using Gameplay.Systems.Chat;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Civics.GameValues;
    using Gameplay.Aliases;
    using Gameplay.Property;

    using Simulation.Time;
    using System.Runtime.CompilerServices;

    [Serialized]
    public class CompaniesData : Singleton<CompaniesData>, IStorage
    {
        public IPersistent StorageHandle { get; set; }

        [Serialized] public Registrar Companies = new Registrar();

        public readonly PeriodicUpdateConfig UpdateTimer = new PeriodicUpdateConfig(true);

        public void InitializeRegistrars()
        {
            this.Companies.Init(Localizer.DoStr("Companies"), true, typeof(Company), CompaniesPlugin.Obj, Localizer.DoStr("Companies"));
        }

        public void Initialize()
        {
            
        }
    }

    [Localized, LocDisplayName(nameof(CompaniesPlugin)), Priority(PriorityAttribute.High)]
    public class CompaniesPlugin : Singleton<CompaniesPlugin>, IModKitPlugin, IInitializablePlugin, IChatCommandHandler, ISaveablePlugin, IContainsRegistrars
    {
        private static readonly IDictionary<Type, GameValueType> gameValueTypeCache = new Dictionary<Type, GameValueType>();

        public readonly CompanyManager CompanyManager;

        [NotNull] private readonly CompaniesData data;

        public CompaniesPlugin()
        {
            data = StorageManager.LoadOrCreate<CompaniesData>("Companies");
            CompanyManager = new CompanyManager();
        }

        public void Initialize(TimedTask timer)
        {
            data.Initialize();
            InstallGameValueHack();
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
        public string GetStatus() => string.Empty;
        public override string ToString() => Localizer.DoStr("Companies");
        public void SaveAll() => StorageManager.Obj.MarkDirty(data);

        #region Chat Commands

        [ChatCommand("Company", ChatAuthorizationLevel.User)]
        public static void Company() { }

        [ChatSubCommand("Company", "Found a new company.", ChatAuthorizationLevel.User)]
        public static void Create(User user, string name)
        {
            var existingEmployer = Companies.Company.GetEmployer(user);
            if (existingEmployer != null)
            {
                user.Player?.OkBoxLoc($"Couldn't found a company as you're already a member of {existingEmployer}");
                return;
            }
            name = name.Trim();
            if (!CompanyManager.Obj.ValidateName(user.Player, name)) { return; }
            var company = CompanyManager.Obj.CreateNew(user, name);
            ChatManager.ServerMessageToAll(Localizer.Do($"{user.UILink()} has founded the company {company.UILink()}!"), Shared.Services.DefaultChatTags.Government, Shared.Services.MessageCategory.Chat);
        }

        [ChatSubCommand("Company", "Invite another player to your company.", ChatAuthorizationLevel.User)]
        public static void Invite(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't send company invite as you are not a CEO of any company");
                return;
            }
            if (user != company.Ceo)
            {
                user.Player?.OkBoxLoc($"Couldn't send company invite as you are not the CEO of {company.MarkedUpName}");
                return;
            }
            company.TryInvite(user.Player, otherUser);
        }

        [ChatSubCommand("Company", "Withdraws an invitation for another player to your company.", ChatAuthorizationLevel.User)]
        public static void Uninvite(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't withdraw company invite as you are not a CEO of any company");
                return;
            }
            if (user != company.Ceo)
            {
                user.Player?.OkBoxLoc($"Couldn't withdraw company invite as you are not the CEO of {company.MarkedUpName}");
                return;
            }
            company.TryUninvite(user.Player, otherUser);
        }

        [ChatSubCommand("Company", "Removes an employee from your company.", ChatAuthorizationLevel.User)]
        public static void Fire(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't fire employee as you are not a CEO of any company");
                return;
            }
            if (user != company.Ceo)
            {
                user.Player?.OkBoxLoc($"Couldn't fire employee as you are not the CEO of {company.MarkedUpName}");
                return;
            }
            company.TryFire(user.Player, otherUser);
        }

        [ChatSubCommand("Company", "Accepts an invite to join a company.", ChatAuthorizationLevel.User)]
        public static void Join(User user, Company company)
        {
            company.TryJoin(user.Player, user);
        }

        [ChatSubCommand("Company", "Resigns you from your current company.", ChatAuthorizationLevel.User)]
        public static void Leave(User user)
        {
            var currentEmployer = Companies.Company.GetEmployer(user);
            if (currentEmployer == null)
            {
                user.Player?.OkBoxLoc($"Couldn't resign from your company as you're not currently employed");
                return;
            }
            currentEmployer.TryLeave(user.Player, user);
        }

        [ChatSubCommand("Company", "Edits the company owned deed that you're currently standing in.", ChatAuthorizationLevel.User)]
        public static void EditDeed(User user)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as you're not currently employed");
                return;
            }
            var deed = PropertyManager.GetDeedWorldPos(user.Position.XZ.Floor);
            if (deed == null)
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as you're not standing on one");
                return;
            }
            if (!company.OwnedDeeds.Contains(deed))
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as it's not owned by {company.MarkedUpName}");
                return;
            }
            DeedEditingUtil.EditInMap(deed, user);
        }

        #endregion
    }
}