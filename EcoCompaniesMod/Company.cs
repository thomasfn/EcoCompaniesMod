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

    using Gameplay.Utils;
    using Gameplay.Systems.Tooltip;
    using Gameplay.Players;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Systems.Messaging.Notifications;
    using Gameplay.Systems.NewTooltip;
    using Gameplay.Items;
    using Gameplay.Economy;
    using Gameplay.GameActions;
    using Gameplay.Aliases;
    using Gameplay.Property;
    using Gameplay.Civics.GameValues;

    using Shared.Serialization;
    using Shared.Localization;
    using Shared.Services;
    using Shared.Items;
    using Shared.IoC;
    using Eco.Gameplay.Settlements;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.Settlements.Components;
    using Eco.Gameplay.Objects;

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
            => Registrars.Get<Company>().Where(x => x.BankAccount == bankAccount).SingleOrDefault();

        public static Company GetFromHQ(Deed deed)
            => Registrars.Get<Company>().Where(x => x.HQDeed == deed).SingleOrDefault();

        [Serialized] public User Ceo { get; set; }

        [Serialized] public User LegalPerson { get; set; }

        [Serialized] public BankAccount BankAccount { get; set; }

        public Deed HQDeed => LegalPerson?.HomesteadDeed;

        public int HQSize => ServiceHolder<SettlementConfig>.Obj.BasePlotsOnHomesteadClaimStake * (CompaniesPlugin.Obj.Config.PropertyLimitsEnabled ? AllEmployees.Count() : 1);

        [Serialized, NotNull] public ThreadSafeHashSet<User> Employees { get; set; } = new ThreadSafeHashSet<User>();

        [Serialized, NotNull] public ThreadSafeHashSet<User> InviteList { get; set; } = new ThreadSafeHashSet<User>();

        public override string IconName => $"Contract";

        public IEnumerable<User> AllEmployees
            => (Ceo != null ? Employees?.Prepend(Ceo) : Employees) ?? Enumerable.Empty<User>();

        public IEnumerable<Deed> OwnedDeeds
            => LegalPerson == null ? Enumerable.Empty<Deed>() :
                PropertyManager.GetAllDeeds()
                    .Where(deed => deed?.Owners?.ContainsUser(LegalPerson) ?? false);

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
                LegalPerson = UserManager.Obj.PrepareNewUser(fakeId, fakeId, $"{Name} Legal Person");
                LegalPerson.Initialize();
            }

            // Setup bank account
            if (BankAccount == null)
            {
                BankAccount = BankAccountManager.Obj.GetPersonalBankAccount(LegalPerson.Name);
                BankAccount.SetName(null, $"{Name} Company Account");
                UpdateBankAccountAuthList(BankAccount);
            }
        }

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
            UpdateBankAccountAuthList(BankAccount);
        }

        private void OnEmployeesChanged()
        {
            UpdateAllAuthLists();
            UpdateHQSize();
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
                    typeof(WorldObject).GetProperty("Creator", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(hostObject, LegalPerson, null);
                    //hostObject.UpdateOwnerName();
                    typeof(WorldObject).GetMethod("UpdateOwnerName", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hostObject, null);
                    if (hostObject.TryGetComponent<HomesteadFoundationComponent>(out var foundationComponent))
                    {
                        typeof(HomesteadFoundationComponent).GetMethod("UpdateTitleAndDesc", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(foundationComponent, null);
                    }
                }
                UpdateHQSize();
                SendCompanyMessage(Localizer.Do($"{this.UILink()} is now the owner of {deed.UILink()}"));
            }
            else
            {
                SendCompanyMessage(Localizer.Do($"{deed.UILink()} is now the new HQ of {this.UILink()}"));
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

        public void OnNoLongerOwnerOfProperty(IEnumerable<Deed> deeds)
        {
            foreach (var deed in deeds)
            {
                OnNoLongerOwnerOfProperty(deed);
            }
            MarkTooltipDirty();
        }

        private void UpdateHQSize()
        {
            if (HQDeed == null) { return; }
            if (!HQDeed.HostObject.TryGetObject(out var worldObj))
            {
                Logger.Debug($"Company '{Name}' had a HQ deed but failed to resolve world object for it when updating HQ size");
                return;
            }
            if (!worldObj.TryGetComponent<PlotsComponent>(out var plotsComponent))
            {
                Logger.Debug($"Company '{Name}' had a HQ deed but failed to resolve plots component for it when updating HQ size");
                return;
            }
            var claimsUpdatedMethod = plotsComponent.GetType().GetMethod("ClaimsUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
            if (claimsUpdatedMethod == null)
            {
                Logger.Error($"Failed to find method PlotsComponent.ClaimsUpdated via reflection");
                return;
            }
            claimsUpdatedMethod.Invoke(plotsComponent, new object[] { null });
        }

        private void UpdateDeedAuthList(Deed deed)
        {
            deed.Accessors.Set(AllEmployees);
            if (deed == HQDeed)
            {
                deed.Residency.Invitations.InvitationList.Set(AllEmployees);
            }
        }

        private void UpdateBankAccountAuthList(BankAccount bankAccount)
        {
            bankAccount.DualPermissions.ManagerSet.Set(Enumerable.Repeat(LegalPerson, 1));
            bankAccount.DualPermissions.UserSet.Set(AllEmployees);
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
            sb.AppendLineLoc($"{BankAccount.UILinkNullSafe()}");
            sb.Append(TextLoc.HeaderLoc($"HQ: "));
            sb.AppendLine(this.HQDeed != null ? this.HQDeed.UILink() : Localizer.DoStr("None."));
            sb.AppendLine(TextLoc.HeaderLoc($"Property:"));
            sb.AppendLine(this.OwnedDeeds.Any() ? this.OwnedDeeds.Where(x => x != HQDeed).Select(x => x.UILinkNullSafe()).InlineFoldoutListLoc("deed", TooltipOrigin.None, 5) : Localizer.DoStr("None."));
            sb.AppendLine(TextLoc.HeaderLoc($"Shareholders:"));
            sb.AppendLine(this.Shareholders.Any() ? this.Shareholders.Select(x => x.Description).InlineFoldoutListLoc("holding", TooltipOrigin.None, 5) : Localizer.DoStr("None."));
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
