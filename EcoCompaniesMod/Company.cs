using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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
    using Eco.Shared.IoC;
    using System.Security.Principal;

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

        [Serialized] public User Ceo { get; set; }

        [Serialized] public User LegalPerson { get; set; }

        [Serialized] public BankAccount BankAccount { get; set; }

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

        public void TryInvite(Player invoker, User user)
        {
            if (InviteList.Contains(user))
            {
                invoker?.OkBoxLoc($"Couldn't invite {user.MarkedUpName} to {MarkedUpName} as they are already invited");
                return;
            }
            if (IsEmployee(user))
            {
                invoker?.OkBoxLoc($"Couldn't invite {user.MarkedUpName} to {MarkedUpName} as they are already an employee");
                return;
            }
            InviteList.Add(user);
            OnInviteListChanged();
            MarkPerUserTooltipDirty(user);
            user.MailLoc($"You have been invited to join {this.UILink()}. Type '/company join {Name}' to accept.", NotificationCategory.Government);
            SendCompanyMessage(Localizer.Do($"{invoker?.User.UILinkNullSafe()} has invited {user.UILink()} to join the company."));
        }

        public void TryUninvite(Player invoker, User user)
        {
            if (!InviteList.Contains(user))
            {
                invoker?.OkBoxLoc($"Couldn't withdraw invite to {user.MarkedUpName} they have not been invited");
                return;
            }
            InviteList.Remove(user);
            OnInviteListChanged();
            MarkPerUserTooltipDirty(user);
            SendCompanyMessage(Localizer.Do($"{invoker?.User.UILinkNullSafe()} has withdrawn the invitation for {user.UILink()} to join the company."));
        }

        public void TryFire(Player invoker, User user)
        {
            if (!IsEmployee(user))
            {
                invoker?.OkBoxLoc($"Couldn't fire {user.MarkedUpName} from {MarkedUpName} as they are not an employee");
                return;
            }
            if (user == Ceo)
            {
                invoker?.OkBoxLoc($"Couldn't fire {user.MarkedUpName} from {MarkedUpName} as they are the CEO");
                return;
            }
            var pack = new GameActionPack();
            pack.AddGameAction(new GameActions.CitizenLeaveCompany
            {
                Citizen = user,
                CompanyLegalPerson = LegalPerson,
                Fired = true,
            });
            pack.AddPostEffect(() =>
            {
                if (!Employees.Remove(user)) { return; }
                OnEmployeesChanged();
                MarkPerUserTooltipDirty(user);
                SendCompanyMessage(Localizer.Do($"{invoker?.User.UILinkNullSafe()} has fired {user.UILink()} from the company."));
            });
            pack.TryPerform(null);
        }

        public void TryJoin(Player invoker, User user)
        {
            var oldEmployer = GetEmployer(user);
            if (oldEmployer != null)
            {
                invoker?.OkBoxLoc($"Couldn't join {MarkedUpName} as you are already employed by {oldEmployer.MarkedUpName}");
                return;
            }
            if (!InviteList.Contains(user))
            {
                invoker?.OkBoxLoc($"Couldn't join {MarkedUpName} as you have not been invited");
                return;
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
        }

        public void TryLeave(Player invoker, User user)
        {
            if (!IsEmployee(user))
            {
                invoker?.OkBoxLoc($"Couldn't resign from {MarkedUpName} as you are not an employee");
                return;
            }
            if (user == Ceo)
            {
                invoker?.OkBoxLoc($"Couldn't resign from {MarkedUpName} as you are the CEO");
                return;
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

        private void OnEmployeesChanged()
        {
            foreach (var deed in OwnedDeeds)
            {
                UpdateDeedAuthList(deed);
            }
            UpdateBankAccountAuthList(BankAccount);
            MarkDirty();
            MarkTooltipDirty();
        }

        public void OnNowOwnerOfProperty(Deed deed)
        {
            SendCompanyMessage(Localizer.Do($"{this.UILink()} is now the owner of {deed.UILink()}"));
            UpdateDeedAuthList(deed);
        }

        public void OnNowOwnerOfProperty(IEnumerable<Deed> deeds)
        {
            foreach (var deed in deeds)
            {
                OnNowOwnerOfProperty(deed);
            }
        }

        public void OnNoLongerOwnerOfProperty(Deed deed)
        {
            SendCompanyMessage(Localizer.Do($"{this.UILink()} is no longer the owner of {deed.UILink()}"));
            deed.Accessors.Clear();
        }

        public void OnNoLongerOwnerOfProperty(IEnumerable<Deed> deeds)
        {
            foreach (var deed in deeds)
            {
                OnNoLongerOwnerOfProperty(deed);
            }
        }

        private void UpdateDeedAuthList(Deed deed)
        {
            deed.Accessors.Set(AllEmployees);
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
            sb.AppendLine(TextLoc.HeaderLoc($"Property:"));
            sb.AppendLine(this.OwnedDeeds.Any() ? this.OwnedDeeds.Select(x => x.UILinkNullSafe()).InlineFoldoutListLoc("deed", TooltipOrigin.None, 5) : Localizer.DoStr("None."));
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
