using System;

namespace Eco.Mods.Companies
{
    using Core.Controller;
    using Core.Utils;
    using Core.Utils.PropertyScanning;

    using Shared.Localization;
    using Shared.Networking;
    using Shared.Utils;

    using Gameplay.Civics.GameValues;
    using Gameplay.Economy;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Players;
    using Gameplay.Aliases;

    [Eco, LocCategory("Companies"), LocDescription("The CEO of the company via a legal person.")]
    public class CompanyCeo : GameValue<User>
    {
        [Eco, Advanced, LocDescription("The legal person of the company to fetch the CEO of.")] public GameValue<User> LegalPerson { get; set; }

        private Eval<User> FailNullSafe<T>(Eval<T> eval, string paramName) =>
            eval != null ? Eval.Make($"Invalid {Localizer.DoStr(paramName)} specified on {GetType().GetLocDisplayName()}: {eval.Message}", null as User)
                         : Eval.Make($"{Localizer.DoStr(paramName)} not set on {GetType().GetLocDisplayName()}.", null as User);

        public override Eval<User> Value(IContextObject action)
        {
            var legalPerson = this.LegalPerson?.Value(action); if (legalPerson?.Val == null) return this.FailNullSafe(legalPerson, nameof(this.LegalPerson));
            var company = Company.GetFromLegalPerson(legalPerson.Val) ?? Company.GetEmployer(legalPerson.Val);

            return Eval.Make<User>($"{company?.Ceo.UILinkNullSafe()} ({company?.UILink()}'s CEO)", company?.Ceo);
        }
        public override LocString Description() => Localizer.Do($"the company of {this.LegalPerson.DescribeNullSafe()}'s CEO");
    }

    [Eco, LocCategory("Companies"), LocDescription("The CEO of the company via a legal person.")]
    [NonSelectable]
    public class CompanyCeoAlias : GameValue<IAlias>
    {
        [Eco, Advanced, LocDescription("The legal person of the company to fetch the CEO of.")] public GameValue<User> LegalPerson { get; set; }

        private Eval<IAlias> FailNullSafe<T>(Eval<T> eval, string paramName) =>
            eval != null ? Eval.Make($"Invalid {Localizer.DoStr(paramName)} specified on {GetType().GetLocDisplayName()}: {eval.Message}", null as IAlias)
                         : Eval.Make($"{Localizer.DoStr(paramName)} not set on {GetType().GetLocDisplayName()}.", null as IAlias);

        public override Eval<IAlias> Value(IContextObject action)
        {
            var legalPerson = this.LegalPerson?.Value(action); if (legalPerson?.Val == null) return this.FailNullSafe(legalPerson, nameof(this.LegalPerson));
            var company = Company.GetFromLegalPerson(legalPerson.Val) ?? Company.GetEmployer(legalPerson.Val);

            return Eval.Make<IAlias>($"{company?.Ceo.UILinkNullSafe()} ({company?.UILink()}'s CEO)", company?.Ceo);
        }
        public override LocString Description() => Localizer.Do($"the company of {this.LegalPerson.DescribeNullSafe()}'s CEO");
    }
}
