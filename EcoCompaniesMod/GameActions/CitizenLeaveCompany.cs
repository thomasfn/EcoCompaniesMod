using System;
using System.Collections.Generic;
using System.Linq;

namespace Eco.Mods.Companies.GameActions
{
    using Gameplay.Players;
    using Gameplay.GameActions;
    using Gameplay.Settlements;

    using Shared.Localization;
    using Shared.Networking;

    [Eco, LocCategory("Companies"), LocDescription("Triggered when a citizen leaves a company.")]
    public class CitizenLeaveCompany : GameAction
    {
        [Eco, LocDescription("The citizen who is leaving the company."), CanAutoAssign] public User Citizen { get; set; }
        [Eco, LocDescription("The legal person of the company."), CanAutoAssign] public User CompanyLegalPerson { get; set; }
        [Eco, LocDescription("If the person is leaving due to being fired.")] public bool Fired { get; set; }

        public override IEnumerable<Settlement> SettlementScopes => CompanyLegalPerson?.AllCitizenships ?? Enumerable.Empty<Settlement>(); //Scope based on company citizenship
    }
}
