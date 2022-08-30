using System;
using System.Text;

namespace Eco.Mods.Companies
{
    using Core.Systems;
    using Core.Utils;

    using Gameplay.Players;
    using Gameplay.GameActions;
    using Gameplay.Property;

    using Shared.Utils;
    using Eco.Gameplay.Auth;
    using Eco.Gameplay.Aliases;

    public class CompanyManager : Singleton<CompanyManager>, IGameActionAware
    {
        public CompanyManager()
        {
            ActionUtil.AddListener(this);
        }

        public bool ValidateName(Player invoker, string name)
        {
            if (name.Length < 3)
            {
                invoker?.OkBoxLoc($"Company name is too short, must be at least 3 characters long");
                return false;
            }
            if (name.Length > 50)
            {
                invoker?.OkBoxLoc($"Company name is too long, must be at most 50 characters long");
                return false;
            }
            return true;
        }

        public Company CreateNew(User ceo, string name)
        {
            var company = Registrars.Add<Company>(null, Registrars.Get<Company>().GetUniqueName(name));
            company.Creator = ceo;
            company.ChangeCeo(ceo);
            company.SaveInRegistrar();
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
            if (action is PropertyTransfer propertyTransferAction)
            {
                // If the deed is company property, allow an employee to transfer ownership
                Company deedOwnerCompany = null;
                foreach (var deed in propertyTransferAction.RelatedDeeds)
                {
                    var ownerCompany = Company.GetFromLegalPerson(deed.Owners);
                    if (ownerCompany == null || (deedOwnerCompany != null && ownerCompany != deedOwnerCompany))
                    {
                        deedOwnerCompany = null;
                        break;
                    }
                    deedOwnerCompany = ownerCompany;
                }
                if (deedOwnerCompany == null) { return LazyResult.FailedNoMessage; }
                if (!deedOwnerCompany.IsEmployee(propertyTransferAction.Citizen)) { return LazyResult.FailedNoMessage; }
                return AuthManagerExtensions.SpecialAccessResult(deedOwnerCompany);
            }
            if (action is ClaimOrUnclaimProperty claimOrUnclaimPropertyAction)
            {
                // If the deed is company property, allow an employee to claim or unclaim it
                var deedOwnerCompany = Company.GetFromLegalPerson(claimOrUnclaimPropertyAction.PreviousDeedOwner);
                if (deedOwnerCompany == null) { return LazyResult.FailedNoMessage; }
                if (!deedOwnerCompany.IsEmployee(claimOrUnclaimPropertyAction.Citizen)) { return LazyResult.FailedNoMessage; }
                return AuthManagerExtensions.SpecialAccessResult(deedOwnerCompany);
            }
            return LazyResult.FailedNoMessage;
        }
    }
}
