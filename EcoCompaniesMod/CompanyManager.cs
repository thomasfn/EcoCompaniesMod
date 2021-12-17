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
                invoker?.OkBoxLoc($"Company name is too long, must be at most 3 characters long");
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
                case MoneyGameAction moneyGameAction:
                    var sourceCompany = Company.GetFromBankAccount(moneyGameAction.SourceBankAccount);
                    if (sourceCompany != null)
                    {
                        sourceCompany.OnGiveMoney(moneyGameAction);
                        //break;
                    }
                    var destCompany = Company.GetFromBankAccount(moneyGameAction.TargetBankAccount);
                    if (destCompany != null)
                    {
                        destCompany.OnReceiveMoney(moneyGameAction);
                        //break;
                    }
                    break;
                case PropertyTransfer propertyTransferAction:
                    var oldOwnerCompany = Company.GetFromLegalPerson(propertyTransferAction.CurrentOwner);
                    if (oldOwnerCompany != null) { oldOwnerCompany.OnNoLongerOwnerOfProperty(propertyTransferAction.RelatedDeeds); }
                    var newOwnerCompany = Company.GetFromLegalPerson(propertyTransferAction.NewOwner);
                    if (newOwnerCompany != null) { newOwnerCompany.OnNowOwnerOfProperty(propertyTransferAction.RelatedDeeds); }
                    break;
            }
        }

        public Result ShouldOverrideAuth(GameAction action)
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
                if (deedOwnerCompany == null) { return null; }
                if (!deedOwnerCompany.IsEmployee(propertyTransferAction.Citizen)) { return null; }
                return AuthManagerExtensions.SpecialAccessResult(deedOwnerCompany);
            }
            if (action is ClaimOrUnclaimProperty claimOrUnclaimPropertyAction)
            {
                // If the deed is company property, allow an employee to claim or unclaim it
                var deedOwnerCompany = Company.GetFromLegalPerson(claimOrUnclaimPropertyAction.PreviousDeedOwner);
                if (deedOwnerCompany == null) { return null; }
                if (!deedOwnerCompany.IsEmployee(claimOrUnclaimPropertyAction.Citizen)) { return null; }
                return AuthManagerExtensions.SpecialAccessResult(deedOwnerCompany);
            }
            return null;
        }
    }
}
