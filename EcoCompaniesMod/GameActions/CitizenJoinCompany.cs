using System;

namespace Eco.Mods.Companies.GameActions
{
    using Gameplay.Players;
    using Gameplay.Economy;
    using Gameplay.GameActions;
    using Gameplay.Civics;

    using Shared.Localization;
    using Shared.Networking;

    [Eco, LocCategory("Companies"), LocDescription("Triggered when a citizen joins a company.")]
    public class CitizenJoinCompany : GameAction
    {
        [Eco, LocDescription("The citizen who transfered the money."), CanAutoAssign] public User Citizen { get; set; }
        [Eco, LocDescription("The legal person of the company who sent the money."), CanAutoAssign] public User CompanyLegalPerson { get; set; }
    }
}
