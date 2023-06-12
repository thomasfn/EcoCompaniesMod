using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Eco.Mods.Companies
{
    using Core.Systems;
    using Core.Utils;

    using Gameplay.Players;
    using Gameplay.GameActions;
    using Gameplay.Property;
    using Gameplay.Systems.Tooltip;
    using Gameplay.Systems.Messaging.Notifications;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Settlements.ClaimStakes;
    using Gameplay.Civics.GameValues;
    using Gameplay.Auth;
    using Gameplay.Aliases;

    using Shared.Utils;
    using Shared.Localization;
    using Shared.Items;
    using Shared.Services;
    using Eco.Gameplay.Settlements.Components;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Settlements;

    public partial class CompanyManager : Singleton<CompanyManager>, IGameActionAware
    {
        [GeneratedRegex("^[\\w][\\w_'. ]+$")]
        private static partial Regex ValidCompanyNameRegex();

        public CompanyManager()
        {
            ActionUtil.AddListener(this);
        }

        public bool ValidateName(string name, out string errorMessage)
        {
            if (name.Length < 3)
            {
                errorMessage = Localizer.DoStr("Company name is too short, must be at least 3 characters long");
                return false;
            }
            if (name.Length > 50)
            {
                errorMessage = Localizer.DoStr("Company name is too long, must be at most 50 characters long");
                return false;
            }
            if (!ValidCompanyNameRegex().IsMatch(name))
            {
                errorMessage = Localizer.DoStr("Company name contains invalid characters, must only contain letters, digits, underscores, apostrophies or full stops, and must start with a character.");
                return false;
            }
            errorMessage = string.Empty;
            return true;
        }

        public readonly struct CreateAttempt : IEquatable<CreateAttempt>
        {
            public static readonly CreateAttempt Invalid = new CreateAttempt();

            public readonly User CEO;
            public readonly string CompanyName;
            public readonly IEnumerable<Deed> TransferDeeds;
            public readonly Settlement JoinSettlement;

            public bool IsValid => CEO != null && !string.IsNullOrEmpty(CompanyName);

            public CreateAttempt(User ceo, string companyName, IEnumerable<Deed> transferDeeds, Settlement joinSettlement)
            {
                CEO = ceo;
                CompanyName = companyName;
                TransferDeeds = transferDeeds;
                JoinSettlement = joinSettlement;
            }

            public override bool Equals(object obj) => obj is CreateAttempt attempt && Equals(attempt);

            public bool Equals(CreateAttempt other)
                => CEO == other.CEO
                && CompanyName == other.CompanyName
                && TransferDeeds.SetEquals(other.TransferDeeds)
                && JoinSettlement == other.JoinSettlement;

            public override int GetHashCode() => HashCode.Combine(CEO, CompanyName, TransferDeeds);

            public static bool operator ==(CreateAttempt left, CreateAttempt right) => left.Equals(right);

            public static bool operator !=(CreateAttempt left, CreateAttempt right) => !(left == right);

            public LocString ToLocString()
                => Localizer.Do($"This will found a company named '{CompanyName}' with {CEO.UILinkNullSafe()} as the CEO.\n{DescribeTransfers()}\n{DescribeJoinSettlement()}");

            private LocString DescribeTransfers()
            {
                if (TransferDeeds?.Any() ?? false)
                {
                    return Localizer.Do($"The following deeds will be transferred to the company upon founding: {TransferDeeds.Select(x => x.UILinkNullSafe()).InlineFoldoutListLoc("deed", TooltipOrigin.None, 5)}");
                }
                return Localizer.DoStr("No deeds will be transferred to the company upon founding.");
            }

            private LocString DescribeJoinSettlement()
            {
                if (JoinSettlement != null)
                {
                    if (JoinSettlement.ImmigrationPolicy?.Approver == null)
                    {
                        return Localizer.Do($"The company will join {JoinSettlement.UILink()} upon founding.");
                    }
                    else
                    {
                        return Localizer.Do($"The company will apply to join {JoinSettlement.UILink()} upon founding.");
                    }
                }
                return Localizer.Do($"The company will not be considered a citizen of any settlement upon founding.");
            }
        }

        public CreateAttempt CreateNewDryRun(User ceo, string name, out string errorMessage)
        {
            var existingEmployer = Company.GetEmployer(ceo);
            if (existingEmployer != null)
            {
                errorMessage = $"Couldn't found a company as you're already a member of {existingEmployer}";
                return CreateAttempt.Invalid;
            }

            name = name.Trim();
            if (!ValidateName(name, out errorMessage)) { return CreateAttempt.Invalid; }
            name = Registrars.Get<Company>().GetUniqueName(name);

            if (Registrars.Get<Company>().GetByName(name) != null)
            {
                errorMessage = $"A company with the name '{name}' already exists";
                return CreateAttempt.Invalid;
            }

            if (Registrars.Get<User>().GetByName(GetLegalPersonName(name)) != null)
            {
                errorMessage = $"A company with the name '{name}' already exists";
                return CreateAttempt.Invalid;
            }

            errorMessage = string.Empty;
            return new CreateAttempt(
                ceo, name,
                CompaniesPlugin.Obj.Config.PropertyLimitsEnabled && ceo.HomesteadDeed != null ? Enumerable.Repeat(ceo.HomesteadDeed, 1) : Enumerable.Empty<Deed>(),
                CompaniesPlugin.Obj.Config.PropertyLimitsEnabled ? ceo.DirectCitizenship : null
            );
        }

        public Company CreateNew(User ceo, string name, CreateAttempt createAttempt, out string errorMessage)
        {
            var latestCreateAttempt = CreateNewDryRun(ceo, name, out errorMessage);
            if (!latestCreateAttempt.IsValid) { return null; }
            if (latestCreateAttempt != createAttempt)
            {
                errorMessage = $"Something changed since you tried to create the company. Please try again.";
                return null;
            }
            var company = Registrars.Add<Company>(null, latestCreateAttempt.CompanyName);
            company.Creator = latestCreateAttempt.CEO;
            company.ChangeCeo(latestCreateAttempt.CEO);
            // TODO: Assign company citienzehip to CEO's federation
            company.SaveInRegistrar();
            if (latestCreateAttempt.TransferDeeds != null)
            {
                foreach (var deed in latestCreateAttempt.TransferDeeds)
                {
                    ClaimHomesteadAsHQ(ceo, deed, company);
                }
            }
            company.UpdateAllAuthLists();
            NotificationManager.ServerMessageToAll(
                Localizer.Do($"{ceo.UILink()} has founded the company {company.UILink()}!"),
                NotificationCategory.Government,
                NotificationStyle.Chat
            );
            if (company.DirectCitizenship == null && createAttempt.JoinSettlement != null)
            {
                if (!company.TryApplyToSettlement(ceo, createAttempt.JoinSettlement, out var joinErr))
                {
                    Logger.Debug($"Company {company.Name} tried to apply to {createAttempt.JoinSettlement.Name} during founding process but failed: '{joinErr}'");
                }
            }
            return company;
        }

        public void ActionPerformed(GameAction action)
        {
            switch (action)
            {
                case GameActions.CompanyExpense:
                case GameActions.CompanyIncome:
                    // Catch these specifically and noop, to avoid them going into the MoneyGameAction case
                    break;
                case MoneyGameAction moneyGameAction:
                    var sourceCompany = Company.GetFromBankAccount(moneyGameAction.SourceBankAccount);
                    sourceCompany?.OnGiveMoney(moneyGameAction);
                    var destCompany = Company.GetFromBankAccount(moneyGameAction.TargetBankAccount);
                    destCompany?.OnReceiveMoney(moneyGameAction);
                    break;
                case PropertyTransfer propertyTransferAction:
                    var oldOwnerCompany = Company.GetFromLegalPerson(propertyTransferAction.CurrentOwner);
                    oldOwnerCompany?.OnNoLongerOwnerOfProperty(propertyTransferAction.RelatedDeeds);
                    var newOwnerCompany = Company.GetFromLegalPerson(propertyTransferAction.NewOwner);
                    newOwnerCompany?.OnNowOwnerOfProperty(propertyTransferAction.RelatedDeeds);
                    break;
            }
        }

        public LazyResult ShouldOverrideAuth(IAlias alias, IOwned property, GameAction action)
        {
            switch (action)
            {
                case PropertyTransfer propertyTransferAction:
                    {
                        // If the deed is company property, allow an employee to transfer ownership, UNLESS it's their HQ
                        Company deedOwnerCompany = null;
                        foreach (var deed in propertyTransferAction.RelatedDeeds)
                        {
                            var ownerCompany = Company.GetFromLegalPerson(deed.Owners);
                            if (ownerCompany == null || deedOwnerCompany != null && ownerCompany != deedOwnerCompany)
                            {
                                deedOwnerCompany = null;
                                break;
                            }
                            deedOwnerCompany = ownerCompany;
                        }
                        if (deedOwnerCompany == null) { return LazyResult.FailedNoMessage; }
                        if (!deedOwnerCompany.IsEmployee(propertyTransferAction.Citizen)) { return LazyResult.FailedNoMessage; }
                        if (CompaniesPlugin.Obj.Config.PropertyLimitsEnabled && deedOwnerCompany.HQDeed != null && propertyTransferAction.RelatedDeeds.Contains(deedOwnerCompany.HQDeed)) { return LazyResult.FailedNoMessage; }
                        return AuthManagerExtensions.SpecialAccessResult(deedOwnerCompany);
                    }

                case ClaimOrUnclaimProperty claimOrUnclaimPropertyAction:
                    {
                        // If the deed is company property, allow an employee to claim or unclaim it
                        var deedOwnerCompany = Company.GetFromLegalPerson(claimOrUnclaimPropertyAction.PreviousDeedOwner);
                        if (deedOwnerCompany == null) { return LazyResult.FailedNoMessage; }
                        if (!deedOwnerCompany.IsEmployee(claimOrUnclaimPropertyAction.Citizen)) { return LazyResult.FailedNoMessage; }
                        return AuthManagerExtensions.SpecialAccessResult(deedOwnerCompany);
                    }
                default:
                    return LazyResult.FailedNoMessage;
            }
        }

        public void InterceptPlaceOrPickupObjectGameAction(PlaceOrPickUpObject placeOrPickUpObject, ref PostResult lawPostResult)
        {
            if (!CompaniesPlugin.Obj.Config.PropertyLimitsEnabled) { return; }

            // Look for attempt to place a new homestead
            if (placeOrPickUpObject.PlacedOrPickedUp == PlacedOrPickedUp.PlacingObject && placeOrPickUpObject.ItemUsed is HomesteadClaimStakeItem)
            {
                // Check if they're employed
                var employer = Company.GetEmployer(placeOrPickUpObject.Citizen);
                if (employer == null) { return; }

                // If the company currently has a HQ, block it
                if (employer.HQDeed != null)
                {
                    lawPostResult.MergeFailLoc($"Can't start a homestead when you're an employee of a company with a HQ");
                    // Due to a bug, this results in a deed still being created, so let's setup a delay and try and fix it
                    Task.Delay(250).ContinueWith((t) => TryFixupDodgyDeeds(placeOrPickUpObject.Citizen));
                    return;
                }

                // This deed will become their HQ
                lawPostResult.AddPostEffect(() =>
                {
                    ClaimHomesteadAsHQ(placeOrPickUpObject.Citizen, employer);
                });
            }

            // Look for attempt to pickup HQ homestead
            if (placeOrPickUpObject.PlacedOrPickedUp == PlacedOrPickedUp.PickingUpObject && placeOrPickUpObject.WorldObject.TryGetComponent<HomesteadFoundationComponent>(out var component))
            {
                var company = Company.GetFromLegalPerson(component.Creator);
                if (company != null && company.IsEmployee(placeOrPickUpObject.Citizen))
                {
                    lawPostResult.AddPostEffect(() =>
                    {
                        FixupHomesteadClaimItems(placeOrPickUpObject.Citizen);
                    });
                }
            }
        }

        private void FixupHomesteadClaimItems(User employee)
        {
            var userField = typeof(HomesteadClaimStakeItem).GetField("user", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (userField == null)
            {
                Logger.Error($"Failed to retrieve 'user' field via reflection on HomesteadClaimStakeItem");
                return;
            }
            var company = Company.GetEmployer(employee);
            if (company == null) { return; }
            // Sweep their inv looking for HomesteadClaimStakeItem items with the "user" field set to the legal person and change it to point at them instead
            foreach (var stack in employee.Inventory.AllInventories.AllStacks())
            {
                if (stack.Item is not HomesteadClaimStakeItem homesteadClaimStakeItem) { continue; }
                if (!homesteadClaimStakeItem.IsUnique) { continue; }
                var currentUser = userField.GetValue(homesteadClaimStakeItem) as User;
                if (currentUser != company.LegalPerson) { continue; }
                userField.SetValue(homesteadClaimStakeItem, employee);
                Logger.Debug($"Fixed up '{stack}' (homestead claim stake) to be keyed to '{employee.Name}' instead of {company.LegalPerson.Name}' after HQ deed was lifted");
            }
        }

        private void ClaimHomesteadAsHQ(User employee, Company employer, bool allowAsyncRetry = true)
        {
            var deed = employee.HomesteadDeed;
            if (deed == null)
            {
                if (allowAsyncRetry)
                {
                    Task.Delay(250).ContinueWith((t) => ClaimHomesteadAsHQ(employee, employer, false));
                    return;
                }
                Logger.Error($"ClaimHomesteadAsHQ failed as employee.HomesteadDeed was null");
                return;
            }
            ClaimHomesteadAsHQ(employee, deed, employer);
        }

        private void TryFixupDodgyDeeds(User user)
        {
            var allOwnedDeeds = PropertyManager.GetAllDeeds()
                .Where(deed => deed?.Owners?.ContainsUser(user) ?? false)
                .ToArray();
            foreach (var deed in allOwnedDeeds)
            {
                if (!deed.HostObject.TryGetObject(out _))
                {
                    Logger.Info($"Found orphaned homestead '{deed.Name}' which wasn't properly cleaned up when an employee tried to place a homestead, cleaning it up now");
                    PropertyManager.ForceRemoveDeed(deed);
                }
            }
        }

        private void ClaimHomesteadAsHQ(User employee, Deed deed, Company employer)
        {
            if (deed.Owner == employer.LegalPerson) { return; }
            deed.ForceChangeOwners(employer.LegalPerson);
            employee.HomesteadDeed = null;
            employer.OnNowOwnerOfProperty(deed);
        }

        internal static string GetLegalPersonName(string companyName)
            => $"{companyName} Legal Person";

        internal static string GetCompanyAccountName(string companyName)
            => $"{companyName} Company Account";

        internal static string GetCompanyCurrencyName(string companyName)
            => $"{companyName} Shares";
    }
}
