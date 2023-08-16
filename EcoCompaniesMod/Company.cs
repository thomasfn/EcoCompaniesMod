using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace Eco.Mods.Companies
{
    using Core.Controller;
    using Core.Systems;
    using Core.Utils;
    using Core.Properties;
    using Core.Serialization;

    using Gameplay.Utils;
    using Gameplay.Players;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Systems.Messaging.Notifications;
    using Gameplay.Systems.NewTooltip;
    using Gameplay.Items;
    using Gameplay.Items.InventoryRelated;
    using Gameplay.Economy;
    using Gameplay.GameActions;
    using Gameplay.Aliases;
    using Gameplay.Property;
    using Gameplay.Civics.GameValues;
    using Gameplay.Settlements;
    using Gameplay.Settlements.Components;
    using Gameplay.Components;
    using Gameplay.Objects;
    using Gameplay.UI;
    using Gameplay.Systems;
    
    using Shared.Serialization;
    using Shared.Localization;
    using Shared.Services;
    using Shared.Items;
    using Shared.IoC;
    using Shared.Utils;

    public readonly struct ShareholderHolding
    {
        public readonly User User;
        public readonly float Share;

        public LocString Description => Localizer.Do($"{User.UILink()}: {Share * 100.0f:N}%");

        public ShareholderHolding(User user, float share)
        {
            User = user;
            Share = share;
        }
    }

    [Serialized, ForceCreateView]
    public class Company : SimpleEntry, IHasIcon
    {
        private bool inReceiveMoney, inGiveMoney;

        public static Company GetEmployer(User user)
            => Registrars.Get<Company>().Where(x => x.IsEmployee(user)).SingleOrDefault();

        public static Company GetFromLegalPerson(User user)
            => Registrars.Get<Company>().Where(x => x.LegalPerson == user).SingleOrDefault();

        public static Company GetFromLegalPerson(IAlias alias)
            => GetFromLegalPerson(alias.OneUser());

        public static Company GetFromBankAccount(BankAccount bankAccount)
            => Registrars.Get<Company>().Where(x => x.DoesOwnBankAccount(bankAccount)).FirstOrDefault();

        public static Company GetFromHQ(Deed deed)
            => Registrars.Get<Company>().Where(x => x.HQDeed == deed).SingleOrDefault();

        [Serialized] public User Ceo { get; set; }

        [Serialized] public User LegalPerson { get; set; }

        [Serialized] public BankAccount BankAccount { get; set; }

        [Serialized] public Currency SharesCurrency { get; set; }

        public Deed HQDeed => LegalPerson?.HomesteadDeed;

        public PlotsComponent HQPlots
        {
            get
            {
                var deed = HQDeed;
                if (deed == null) { return null; }
                if (!deed.HostObject.TryGetObject(out var worldObj)) { return null; }
                if (!worldObj.TryGetComponent<PlotsComponent>(out var plotsComponent)) { return null; }
                return plotsComponent;
            }
        }

        public int HQSize => ServiceHolder<SettlementConfig>.Obj.BasePlotsOnHomesteadClaimStake * (CompaniesPlugin.Obj.Config.PropertyLimitsEnabled ? AllEmployees.Count() : 1);

        public Settlement DirectCitizenship => LegalPerson.DirectCitizenship;

        public IEnumerable<Settlement> AllCitizenships => LegalPerson.AllCitizenships;

        [Serialized, NotNull] public ThreadSafeHashSet<User> Employees { get; set; } = new ThreadSafeHashSet<User>();

        [Serialized, NotNull] public ThreadSafeHashSet<User> InviteList { get; set; } = new ThreadSafeHashSet<User>();

        public override string IconName => $"Contract";

        public IEnumerable<User> AllEmployees
            => (Ceo != null ? Employees?.Prepend(Ceo) : Employees) ?? Enumerable.Empty<User>();

        public IEnumerable<Deed> OwnedDeeds
            => LegalPerson == null ? Enumerable.Empty<Deed>() :
                PropertyManager.GetAllDeeds()
                    .Where(deed => deed?.Owners?.ContainsUser(LegalPerson) ?? false);

        public IEnumerable<BankAccount> OwnedAccounts
            => LegalPerson == null ? Enumerable.Empty<BankAccount>() :
                BankAccountManager.Obj.ManagedAccounts(LegalPerson)
                    .Where(account => account == BankAccount || (account is not PersonalBankAccount && account is not GovernmentBankAccount));

        public IEnumerable<ShareholderHolding> Shareholders =>
            Ceo != null ? Enumerable.Repeat(new ShareholderHolding(Ceo, 1.0f), 1) : Enumerable.Empty<ShareholderHolding>();

        public override void Initialize()
        {
            base.Initialize();

            // Setup employees
            if (Employees == null)
            {
                Employees = new ThreadSafeHashSet<User>();
            }

            // Setup legal person
            if (LegalPerson == null)
            {
                string fakeId = Guid.NewGuid().ToString();
                LegalPerson = UserManager.Obj.PrepareNewUser(fakeId, fakeId, Registrars.Get<User>().GetUniqueName(CompanyManager.GetLegalPersonName(Name)));
                LegalPerson.Initialize();
            }
            SettlementCommon.Initializer.RunIfOrWhenInitialized(() =>
            {
                this.WatchProp(LegalPerson, nameof(User.DirectCitizenship), (_, ev) =>
                {
                    OnLegalPersonCitizenshipChanged(ev);
                });
            });

            // Setup bank account
            if (BankAccount == null)
            {
                BankAccount = BankAccountManager.Obj.GetPersonalBankAccount(LegalPerson.Name);
                BankAccount.SetName(null, Registrars.Get<BankAccount>().GetUniqueName(CompanyManager.GetCompanyAccountName(Name)));
                UpdateBankAccountAuthList(BankAccount);
            }

            // Setup shares currency
            if (SharesCurrency == null)
            {
                SharesCurrency = CurrencyManager.GetPlayerCurrency(LegalPerson);
                SharesCurrency.SetName(null, Registrars.Get<Currency>().GetUniqueName(CompanyManager.GetCompanyCurrencyName(Name)));
            }

            // Setup HQ deed
            HQPlots?.OnChanged.AddUnique(FixupHQSize);
            ForceUpdateHQSize();
        }

        public bool DoesOwnBankAccount(BankAccount bankAccount)
            => bankAccount == BankAccount || (bankAccount is not PersonalBankAccount && bankAccount is not GovernmentBankAccount && bankAccount.DualPermissions.CanAccess(LegalPerson, AccountAccess.Manage));

        #region Employee Management

        public bool TryInvite(User invoker, User target, out LocString errorMessage)
        {
            if (invoker != Ceo)
            {
                errorMessage = Localizer.DoStr($"Couldn't invite {target.MarkedUpName} to {MarkedUpName} as you are not the CEO of {MarkedUpName}");
                return false;
            }
            if (InviteList.Contains(target))
            {
                errorMessage = Localizer.DoStr($"Couldn't invite {target.MarkedUpName} to {MarkedUpName} as they are already invited");
                return false;
            }
            if (IsEmployee(target))
            {
                errorMessage = Localizer.DoStr($"Couldn't invite {target.MarkedUpName} to {MarkedUpName} as they are already an employee");
                return false;
            }
            InviteList.Add(target);
            OnInviteListChanged();
            MarkPerUserTooltipDirty(target);
            target.MailLoc($"You have been invited to join {this.UILink()}. Type '/company join {Name}' to accept.", NotificationCategory.Government);
            SendCompanyMessage(Localizer.Do($"{invoker.UILinkNullSafe()} has invited {target.UILink()} to join the company."));
            errorMessage = LocString.Empty;
            return true;
        }

        public bool TryUninvite(User invoker, User target, out LocString errorMessage)
        {
            if (invoker != Ceo)
            {
                errorMessage = Localizer.DoStr($"Couldn't withdraw invite of {target.MarkedUpName} to {MarkedUpName} as you are not the CEO of {MarkedUpName}");
                return false;
            }
            if (!InviteList.Contains(target))
            {
                errorMessage = Localizer.DoStr($"Couldn't withdraw invite of {target.MarkedUpName} to {MarkedUpName} they have not been invited");
                return false;
            }
            InviteList.Remove(target);
            OnInviteListChanged();
            MarkPerUserTooltipDirty(target);
            SendCompanyMessage(Localizer.Do($"{invoker.UILinkNullSafe()} has withdrawn the invitation for {target.UILink()} to join the company."));
            errorMessage = LocString.Empty;
            return true;
        }

        public bool TryFire(User invoker, User target, out LocString errorMessage)
        {
            if (invoker != Ceo)
            {
                errorMessage = Localizer.DoStr($"Couldn't fire {target.MarkedUpName} from {MarkedUpName} as you are not the CEO of {MarkedUpName}");
                return false;
            }
            if (!IsEmployee(target))
            {
                errorMessage = Localizer.DoStr($"Couldn't fire {target.MarkedUpName} from {MarkedUpName} as they are not an employee");
                return false;
            }
            if (target == Ceo)
            {
                errorMessage = Localizer.DoStr($"Couldn't fire {target.MarkedUpName} from {MarkedUpName} as they are the CEO");
                return false;
            }
            var pack = new GameActionPack();
            pack.AddGameAction(new GameActions.CitizenLeaveCompany
            {
                Citizen = target,
                CompanyLegalPerson = LegalPerson,
                Fired = true,
            });
            pack.AddPostEffect(() =>
            {
                if (!Employees.Remove(target)) { return; }
                OnEmployeesChanged();
                MarkPerUserTooltipDirty(target);
                SendCompanyMessage(Localizer.Do($"{invoker.UILinkNullSafe()} has fired {target.UILink()} from the company."));
            });
            pack.TryPerform(null);
            errorMessage = LocString.Empty;
            return true;
        }

        public bool TryJoin(User user, out LocString errorMessage)
        {
            var oldEmployer = GetEmployer(user);
            if (oldEmployer != null)
            {
                errorMessage = Localizer.Do($"Couldn't join {MarkedUpName} as you are already employed by {oldEmployer.MarkedUpName}.\nYou must leave {oldEmployer.MarkedUpName} before joining {MarkedUpName}.");
                return false;
            }
            if (!InviteList.Contains(user))
            {
                errorMessage = Localizer.Do($"Couldn't join {MarkedUpName} as you have not been invited.");
                return false;
            }
            if (CompaniesPlugin.Obj.Config.PropertyLimitsEnabled && user.HomesteadDeed != null)
            {
                errorMessage = Localizer.Do($"Couldn't join {MarkedUpName} as you have a homestead deed.\nYou must remove {user.HomesteadDeed.UILink()} before joining {MarkedUpName}.");
                return false;
            }
            var pack = new GameActionPack();
            pack.AddGameAction(new GameActions.CitizenJoinCompany
            {
                Citizen = user,
                CompanyLegalPerson = LegalPerson,
            });
            pack.AddPostEffect(() =>
            {
                if (!InviteList.Remove(user)) { return; }
                if (!Employees.Add(user)) { return; }
                OnEmployeesChanged();
                MarkPerUserTooltipDirty(user);
                SendCompanyMessage(Localizer.Do($"{user.UILink()} has joined the company."));
            });
            pack.TryPerform(null);
            errorMessage = LocString.Empty;
            return true;
        }

        public bool TryLeave(User user, out LocString errorMessage)
        {
            if (!IsEmployee(user))
            {
                errorMessage = Localizer.Do($"Couldn't resign from {MarkedUpName} as you are not an employee");
                return false;
            }
            if (user == Ceo)
            {
                errorMessage = Localizer.Do($"Couldn't resign from {MarkedUpName} as you are the CEO");
                return false;
            }
            var pack = new GameActionPack();
            pack.AddGameAction(new GameActions.CitizenLeaveCompany
            {
                Citizen = user,
                CompanyLegalPerson = LegalPerson,
                Fired = false,
            });
            pack.AddPostEffect(() =>
            {
                if (!Employees.Remove(user)) { return; }
                OnEmployeesChanged();
                MarkPerUserTooltipDirty(user);
                SendCompanyMessage(Localizer.Do($"{user.UILink()} has resigned from the company."));
            });
            pack.TryPerform(null);
            errorMessage = LocString.Empty;
            return true;
        }

        #endregion

        #region Citizenship Management

        private bool CheckLegalPersonCanJoinSettlement(Settlement target, out LocString errorMessage)
        {
            var canJoinResult = target.ImmigrationPolicy?.CheckCanJoinAsCitizen(LegalPerson) ?? Result.Succeeded;
            if (!canJoinResult.Success)
            {
                errorMessage = canJoinResult.Message;
                return false;
            }
            var canBeMemberEventDelegate = (MulticastDelegate)typeof(UserRoster)
                .GetField(nameof(UserRoster.CanBeMember), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .GetValue(target.Citizenship.DirectCitizenRoster);
            if (canBeMemberEventDelegate == null)
            {
                Logger.Error($"Failed to retrieve the CanBeMember event delegate from UserRoster");
                errorMessage = Localizer.DoStr($"Couldn't join {target.MarkedUpName} due to an internal error");
                return false;
            }
            var canBeMemberResult = (bool)canBeMemberEventDelegate.DynamicInvoke(LegalPerson, null);
            if (!canBeMemberResult)
            {
                errorMessage = Localizer.DoStr($"Couldn't join {target.MarkedUpName} as {MarkedUpName} is already a member of another settlement and has property there");
                return false;
            }
            errorMessage = LocString.Empty;
            return true;
        }

        private bool CheckLegalPersonCanLeaveSettlement(out LocString errorMessage)
        {
            var topParent = DirectCitizenship.TopParent();
            foreach (var settlement in topParent.AllChildrenSettlementsRecursive())
            {
                var currentImmigrationPolicies = settlement.ImmigrationPolicy;
                var result = currentImmigrationPolicies.CanLeaveWithProperties(LegalPerson, out var deedsInSettlement);

                if (!result)
                {
                    errorMessage = result.Message;
                    return false;
                }
            }
            if (!(bool)typeof(SettlementCitizenship)
                .GetMethod("PreventUserWithHomesteadFromLeaving", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(DirectCitizenship.Citizenship, new object[] { LegalPerson, false }))
            {
                errorMessage = Localizer.DoStr($"Couldn't leave {DirectCitizenship.MarkedUpName} as {MarkedUpName} holds property there.");
                return false;
            }
            errorMessage = LocString.Empty;
            return true;
        }

        public bool TryApplyToSettlement(User invoker, Settlement target, out LocString errorMessage)
        {
            if (invoker != Ceo)
            {
                errorMessage = Localizer.DoStr($"Couldn't apply to join {target.MarkedUpName} as you are not the CEO of {MarkedUpName}");
                return false;
            }
            if (!target.Citizenship.DirectCitizenRoster.CanApply(LegalPerson))
            {
                errorMessage = Localizer.DoStr($"Couldn't apply to join {target.MarkedUpName} as {MarkedUpName} has already applied or been invited, or {target.MarkedUpName} is not currently accepting new applicants.");
                return false;
            }
            if (!CheckLegalPersonCanJoinSettlement(target, out errorMessage)) { return false; }
            var approver = target.ImmigrationPolicy?.Approver;
            if (approver == null)
            {
                target.Citizenship.DirectCitizenRoster.AddToRoster(null, LegalPerson, true);
                errorMessage = LocString.Empty;
                return true;
            }
            target.Citizenship.DirectCitizenRoster.Applicants.Add(LegalPerson);
            SendCompanyMessage(Localizer.Do($"{MarkedUpName} has applied to join {target.UILink()}."));
            if (!target.HostObject.TryGetObject(out var worldObject)) { worldObject = null; }
            approver.MailLoc($"{MarkedUpName} has applied to be a Citizen of {target.MarkedUpName}. You may approve or reject this application on {worldObject?.MarkedUpName}.", NotificationCategory.Notifications);
            errorMessage = LocString.Empty;
            return true;
        }

        public bool TryJoinSettlement(User invoker, Settlement target, out LocString errorMessage)
        {
            if (invoker != Ceo)
            {
                errorMessage = Localizer.DoStr($"Couldn't try to join {target.MarkedUpName} as you are not the CEO of {MarkedUpName}");
                return false;
            }
            if (!CheckLegalPersonCanJoinSettlement(target, out errorMessage)) { return false; }
            if (target.ImmigrationPolicy?.Approver == null)
            {
                target.Citizenship.DirectCitizenRoster.AddToRoster(null, LegalPerson, true);
                errorMessage = LocString.Empty;
                return true;
            }
            if (!target.Citizenship.DirectCitizenRoster.CanAcceptInvitation(LegalPerson))
            {
                errorMessage = Localizer.DoStr($"Couldn't try to join {target.MarkedUpName} as {MarkedUpName} has not been invited.");
                return false;
            }
            
            target.Citizenship.DirectCitizenRoster.AddToRoster(null, LegalPerson, false);
            errorMessage = LocString.Empty;
            return true;
        }

        public bool TryLeaveSettlement(User invoker, out LocString errorMessage)
        {
            if (DirectCitizenship == null)
            {
                errorMessage = Localizer.DoStr($"{MarkedUpName} is not currently part of any settlement.");
                return false;
            }
            if (invoker != Ceo)
            {
                errorMessage = Localizer.DoStr($"Couldn't leave {DirectCitizenship.MarkedUpName} from {MarkedUpName} as you are not the CEO of {MarkedUpName}");
                return false;
            }
            if (!DirectCitizenship.Citizenship.DirectCitizenRoster.CanLeave(LegalPerson))
            {
                errorMessage = Localizer.DoStr($"Couldn't leave {DirectCitizenship.MarkedUpName} as {MarkedUpName} is not currently a citizen.");
                return false;
            }
            if (!CheckLegalPersonCanLeaveSettlement(out errorMessage)) { return false; }
            var settlement = DirectCitizenship;
            settlement.Citizenship.DirectCitizenRoster.ForceRemoveMember(LegalPerson);
            errorMessage = LocString.Empty;
            return true;
        }

        #endregion

        public void OnReceiveMoney(MoneyGameAction moneyGameAction)
        {
            if (inReceiveMoney) { return; }
            inReceiveMoney = true;
            try
            {
                var pack = new GameActionPack();
                pack.AddGameAction(new GameActions.CompanyIncome
                {
                    SourceBankAccount = moneyGameAction.SourceBankAccount,
                    TargetBankAccount = moneyGameAction.TargetBankAccount,
                    Currency = moneyGameAction.Currency,
                    CurrencyAmount = moneyGameAction.CurrencyAmount,
                    ReceiverLegalPerson = LegalPerson,
                });
                pack.TryPerform(null);
            }
            finally
            {
                inReceiveMoney = false;
            }
        }

        public void OnGiveMoney(MoneyGameAction moneyGameAction)
        {
            if (inGiveMoney) { return; }
            inGiveMoney = true;
            try
            {
                var pack = new GameActionPack();
                pack.AddGameAction(new GameActions.CompanyExpense
                {
                    SourceBankAccount = moneyGameAction.SourceBankAccount,
                    TargetBankAccount = moneyGameAction.TargetBankAccount,
                    Currency = moneyGameAction.Currency,
                    CurrencyAmount = moneyGameAction.CurrencyAmount,
                    SenderLegalPerson = LegalPerson,
                });
                pack.TryPerform(null);
            }
            finally
            {
                inGiveMoney = false;
            }
        }

        private void OnInviteListChanged()
        {
            MarkDirty();
            MarkTooltipDirty();
        }

        public void UpdateAllAuthLists()
        {
            foreach (var deed in OwnedDeeds)
            {
                UpdateDeedAuthList(deed);
            }
            foreach (var account in OwnedAccounts)
            {
                UpdateBankAccountAuthList(account);
            }
        }

        public void UpdateCitizenships()
        {
            if (!CompaniesPlugin.Obj.Config.PropertyLimitsEnabled) { return; }
            foreach (var user in AllEmployees)
            {
                UpdateCitizenship(user);
            }
        }

        private void UpdateCitizenship(User user)
        {
            if (DirectCitizenship != null)
            {
                // Company has a citizenship, ensure user inherits it
                if (user.DirectCitizenship == null)
                {
                    DirectCitizenship.Citizenship.DirectCitizenRoster.AddToRoster(null, user, true);
                }
                else if (user.DirectCitizenship != DirectCitizenship)
                {
                    user.DirectCitizenship.Citizenship.DirectCitizenRoster.Leave(user, true);
                    DirectCitizenship.Citizenship.DirectCitizenRoster.AddToRoster(null, user, true);
                }
            }
            else
            {
                // Company has no citizenship, ensure user inherits it
                if (user.DirectCitizenship != null)
                {
                    user.DirectCitizenship.Citizenship.DirectCitizenRoster.Leave(user, true);
                }
            }
        }

        public void OnLegalPersonGainedVoidStorage(VoidStorageWrapper voidStorage)
        {
            // Logger.Debug($"{LegalPerson.Name} now has a new void storage {voidStorage.Name}, authing all employees...");
            voidStorage.CanAccess.AddUniqueRange(AllEmployees);
        }

        private void UpdateVoidStorages()
        {
            foreach (var voidStorage in GlobalData.Obj.VoidStorageManager.VoidStorages.Where(x => x.CanUserAccess(LegalPerson)))
            {
                OnLegalPersonGainedVoidStorage(voidStorage);
            }
            GlobalData.Obj.VoidStorageManager.Changed(nameof(GlobalData.Obj.VoidStorageManager.AccessibleVoidStorages));
            GameData.Obj.SaveAll();
        }

        private void OnEmployeesChanged()
        {
            UpdateAllAuthLists();
            ForceUpdateHQSize();
            MarkDirty();
            MarkTooltipDirty();
        }

        public void OnNowOwnerOfProperty(Deed deed)
        {
            if (deed.IsHomesteadDeed)
            {
                LegalPerson.HomesteadDeed = deed;
                Registrars.Get<Deed>().Rename(deed, $"{Name} HQ", true);
                
                if (deed.HostObject.TryGetObject(out var hostObject))
                {
                    //hostObject.Creator = LegalPerson;
                    typeof(WorldObject)
                        .GetProperty("Creator", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(hostObject, LegalPerson, null);
                    //hostObject.UpdateOwnerName();
                    typeof(WorldObject)
                        .GetMethod("UpdateOwnerName", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(hostObject, null);
                    deed.UpdateInfluencingSettlement();
                    
                    if (hostObject.TryGetComponent<HomesteadFoundationComponent>(out var foundationComponent))
                    {
                        // foundationComponent.CitizenshipUpdated(true);
                        typeof(HomesteadFoundationComponent)
                            .GetMethod("CitizenshipUpdated", BindingFlags.NonPublic | BindingFlags.Instance)
                            .Invoke(foundationComponent, new object[] { true });
                    }
                    if (hostObject.TryGetComponent<PlotsComponent>(out var plotsComponent))
                    {
                        plotsComponent.OnChanged.AddUnique(FixupHQSize);
                    }
                }
                SetCitizenOf(deed.CachedAssignedSettlementOfStake);
                ForceUpdateHQSize();
                SendCompanyMessage(Localizer.Do($"{deed.UILink()} is now the new HQ of {this.UILink()}"));
            }
            else
            {
                SendCompanyMessage(Localizer.Do($"{this.UILink()} is now the owner of {deed.UILink()}"));
            }
            UpdateDeedAuthList(deed);
        }

        public void OnNowOwnerOfProperty(IEnumerable<Deed> deeds)
        {
            foreach (var deed in deeds)
            {
                OnNowOwnerOfProperty(deed);
            }
            MarkTooltipDirty();
        }

        public void OnNoLongerOwnerOfProperty(Deed deed)
        {
            if (deed == HQDeed)
            {
                HQPlots?.OnChanged.Remove(FixupHQSize);
                LegalPerson.HomesteadDeed = null;
                SendCompanyMessage(Localizer.Do($"{deed.UILink()} is no longer the HQ of {this.UILink()}"));
                deed.Residency.Invitations.InvitationList.Clear();
            }
            else
            {
                SendCompanyMessage(Localizer.Do($"{this.UILink()} is no longer the owner of {deed.UILink()}"));
            }
            deed.Accessors.Clear();
        }

        private void OnLegalPersonCitizenshipChanged(Core.PropertyHandling.MemberChangedBeforeAfterEventArgs ev)
        {
            if (ev.Before is Settlement beforeSettlement)
            {
                if (DirectCitizenship != null)
                {
                    SendCompanyMessage(Localizer.Do($"{MarkedUpName} has left {beforeSettlement.UILink()} and joined {DirectCitizenship.UILink()}."));
                }
                else
                {
                    SendCompanyMessage(Localizer.Do($"{MarkedUpName} has left {beforeSettlement.UILink()}."));
                }
            }
            else if (DirectCitizenship != null)
            {
                SendCompanyMessage(Localizer.Do($"{MarkedUpName} has joined {DirectCitizenship.UILink()}."));
            }
            UpdateCitizenships();
            MarkTooltipDirty();
            LegalPerson.MarkDirty();
        }

        public void OnNoLongerOwnerOfProperty(IEnumerable<Deed> deeds)
        {
            foreach (var deed in deeds)
            {
                OnNoLongerOwnerOfProperty(deed);
            }
            MarkTooltipDirty();
        }

        public bool CheckCitizenshipDesync(out LocString errorMessage)
        {
            if (DirectCitizenship != null && !DirectCitizenship.Citizenship.HasCitizen(LegalPerson))
            {
                errorMessage = Localizer.Do($"{this.UILink()} was a citizen of {DirectCitizenship.UILink()} but not on the roster, removing...");
                LegalPerson.DirectCitizenship = null;
                return true;
            }
            foreach (var settlement in Registrars.All<Settlement>())
            {
                if (settlement.Citizenship.HasCitizen(LegalPerson))
                {
                    if (DirectCitizenship == settlement) { break; }
                    errorMessage = Localizer.Do($"{this.UILink()} was on the roster for {settlement.UILink()} but not a citizen of, updating...");
                    LegalPerson.DirectCitizenship = settlement;
                    return true;
                }
            }
            errorMessage = LocString.Empty;
            return false;
        }

        private void SetCitizenOf(Settlement settlement)
        {
            if (DirectCitizenship == settlement) { return; }
            if (DirectCitizenship != null)
            {
                DirectCitizenship.Citizenship.DirectCitizenRoster.Leave(LegalPerson);
            }
            if (settlement != null)
            {
                settlement.Citizenship.DirectCitizenRoster.AddToRoster(null, LegalPerson, true);
            }
            LegalPerson.DirectCitizenship = settlement;
        }

        public bool CheckHQDesync(out LocString errorMessage)
        {
            var ownedHomesteadDeeds = OwnedDeeds.Where(d => d.IsHomesteadDeed).ToArray();
            if (ownedHomesteadDeeds.Length == 0 && HQDeed != null)
            {
                errorMessage = Localizer.Do($"Detected incorrectly assigned HQ deed '{HQDeed.UILink()}' (deed was not owned by legal person), clearing...");
                OnNoLongerOwnerOfProperty(HQDeed);
                return true;
            }
            if (ownedHomesteadDeeds.Length > 0)
            {
                if (ownedHomesteadDeeds[0] != HQDeed)
                {
                    errorMessage = Localizer.Do($"Detected unassigned HQ deed '{ownedHomesteadDeeds[0].UILink()}' (deed was owned by legal person but not set as homestead), updating...");
                    OnNowOwnerOfProperty(ownedHomesteadDeeds[0]);
                    return true;
                }
            }
            errorMessage = LocString.Empty;
            return false;
        }

        public void ForceUpdateHQSize()
        {
            var plotsComponent = HQPlots;
            if (plotsComponent == null) { return; }
            plotsComponent.OnChanged.AddUnique(FixupHQSize);
            var claimsUpdatedMethod = typeof(PlotsComponent)
                .GetMethod("ClaimsUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
            if (claimsUpdatedMethod == null)
            {
                Logger.Error($"Failed to find method PlotsComponent.ClaimsUpdated via reflection");
                return;
            }
            claimsUpdatedMethod.Invoke(plotsComponent, new object[] { null });
        }

        private void FixupHQSize()
        {
            var deed = HQDeed;
            var plotsComponent = HQPlots;
            if (deed == null || plotsComponent == null) { return; }
            var originalValue = deed.AllowedPlots;
            var newAllowedPlots = (originalValue - ServiceHolder<SettlementConfig>.Obj.BasePlotsOnHomesteadClaimStake) + HQSize;
            if (deed.AllowedPlots == newAllowedPlots) { return; }
            deed.AllowedPlots = newAllowedPlots;
            plotsComponent.UpdateDescription();
            Logger.Debug($"Company.FixupHQSize: Overriding HQ size of '{deed.Name}' from {originalValue} to {HQSize}");
        }

        private void UpdateDeedAuthList(Deed deed)
        {
            deed.Accessors.Set(AllEmployees);
            if (deed == HQDeed)
            {
                deed.Residency.Invitations.InvitationList.Set(AllEmployees);
            }
        }

        public void UpdateBankAccountAuthList(BankAccount bankAccount)
        {
            bankAccount.DualPermissions.ManagerSet.Set(Enumerable.Repeat(LegalPerson, 1));
            bankAccount.DualPermissions.UserSet.Set(AllEmployees);
            if (bankAccount != BankAccount) { MarkTooltipDirty(); }
        }

        private void MarkTooltipDirty()
        {
            ServiceHolder<ITooltipSubscriptions>.Obj.MarkTooltipPartDirty(nameof(Tooltip), instance: this);
        }

        private void MarkPerUserTooltipDirty(User user)
        {
            ServiceHolder<ITooltipSubscriptions>.Obj.MarkTooltipPartDirty(nameof(PerUserTooltip), instance: this, user: user);
        }

        public void ChangeCeo(User newCeo)
        {
            Ceo = newCeo;
            SendGlobalMessage(Localizer.Do($"{newCeo.UILink()} is now the CEO of {this.UILink()}!"));
            OnEmployeesChanged();
        }

        public void SendCompanyMessage(LocString message, NotificationCategory notificationCategory = NotificationCategory.Government, NotificationStyle notificationStyle = NotificationStyle.Chat)
        {
            foreach (var user in AllEmployees)
            {
                NotificationManager.ServerMessageToPlayer(
                    message,
                    user,
                    notificationCategory,
                    notificationStyle
                );
            }
        }

        private static void SendGlobalMessage(LocString message)
        {
            NotificationManager.ServerMessageToAll(
                message,
                NotificationCategory.Government,
                NotificationStyle.Chat
            );
        }

        public bool IsEmployee(User user)
            => AllEmployees.Contains(user);

        public override LocString CreatorText(Player reader) => this.Creator != null ? Localizer.Do($"Company founded by {this.Creator.MarkedUpName}.") : LocString.Empty;

        [NewTooltip(CacheAs.Instance, 100)]
        public LocString Tooltip()
        {
            var sb = new LocStringBuilder();
            sb.Append(TextLoc.HeaderLoc($"CEO: "));
            sb.AppendLine(Ceo.UILinkNullSafe());
            sb.AppendLine(TextLoc.HeaderLoc($"Employees:"));
            sb.AppendLine(this.Employees.Any() ? this.Employees.Select(x => x.UILinkNullSafe()).InlineFoldoutListLoc("citizen", TooltipOrigin.None, 5) : Localizer.DoStr("None."));
            sb.Append(TextLoc.HeaderLoc($"Finances: "));
            sb.AppendLine(this.OwnedAccounts.Any() ? this.OwnedAccounts.Select(x => x.UILinkNullSafe()).InlineFoldoutListLoc("account", TooltipOrigin.None, 5) : Localizer.DoStr("None."));
            sb.Append(TextLoc.HeaderLoc($"HQ: "));
            sb.AppendLine(this.HQDeed != null ? this.HQDeed.UILink() : Localizer.DoStr("None."));
            sb.AppendLine(TextLoc.HeaderLoc($"Property:"));
            sb.AppendLine(this.OwnedDeeds.Any() ? this.OwnedDeeds.Where(x => x != HQDeed).Select(x => x.UILinkNullSafe()).InlineFoldoutListLoc("deed", TooltipOrigin.None, 5) : Localizer.DoStr("None."));
            sb.AppendLine(TextLoc.HeaderLoc($"Shareholders:"));
            sb.AppendLine(this.Shareholders.Any() ? this.Shareholders.Select(x => x.Description).InlineFoldoutListLoc("holding", TooltipOrigin.None, 5) : Localizer.DoStr("None."));
            sb.Append(TextLoc.HeaderLoc($"Citizenship: "));
            sb.AppendLine(this.DirectCitizenship != null ? DirectCitizenship.UILink() : Localizer.DoStr("None."));
            return sb.ToLocString();
        }

        [NewTooltip(CacheAs.Instance | CacheAs.User, 110)]
        public LocString PerUserTooltip(User user)
        {
            var sb = new LocStringBuilder();
            if (user == Ceo)
            {
                return Localizer.DoStr("You are the CEO of this company.");
            }
            else if (IsEmployee(user))
            {
                return Localizer.DoStr("You are an employee of this company.");
            }
            else if (InviteList.Contains(user))
            {
                return Localizer.DoStr("You have been invited to join this company.");
            }
            else
            {
                return Localizer.DoStr("You are not an employee of this company.");
            }
        }
    }
}
