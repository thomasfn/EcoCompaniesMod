using System;

namespace Eco.Mods.Companies.GameActions
{
    using Gameplay.Players;
    using Gameplay.Economy;
    using Gameplay.GameActions;
    using Gameplay.Civics;

    using Shared.Localization;
    using Shared.Networking;

    [Eco, LocCategory("Companies"), LocDescription("Triggered when a citizen leaves a company.")]
    public class CitizenLeaveCompany : GameAction
    {
        [Eco, LocDescription("The citizen who transfered the money."), CanAutoAssign] public User Citizen { get; set; }
        [Eco, LocDescription("The legal person of the company who sent the money."), CanAutoAssign] public User CompanyLegalPerson { get; set; }
        [Eco, LocDescription("If the person is leaving due to being fired.")] public bool Fired { get; set; }
    }
}
