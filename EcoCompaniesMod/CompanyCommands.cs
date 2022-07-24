using System;
using System.Linq;

namespace Eco.Mods.Companies
{
    using Shared.Localization;
    using Shared.Services;
    using Shared.Math;

    using Gameplay.Players;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Property;
    using Gameplay.Systems.Messaging.Chat.Commands;
    using Gameplay.Systems.Messaging.Notifications;

    [ChatCommandHandler]
    public static class CompanyCommands
    {
        [ChatCommand("Company", ChatAuthorizationLevel.User)]
        public static void Company() { }

        [ChatSubCommand("Company", "Found a new company.", ChatAuthorizationLevel.User)]
        public static void Create(User user, string name)
        {
            var existingEmployer = Companies.Company.GetEmployer(user);
            if (existingEmployer != null)
            {
                user.Player?.OkBoxLoc($"Couldn't found a company as you're already a member of {existingEmployer}");
                return;
            }
            name = name.Trim();
            if (!CompanyManager.Obj.ValidateName(user.Player, name)) { return; }
            var company = CompanyManager.Obj.CreateNew(user, name);
            NotificationManager.ServerMessageToAll(
                Localizer.Do($"{user.UILink()} has founded the company {company.UILink()}!"),
                NotificationCategory.Government,
                NotificationStyle.Chat
            );
        }

        [ChatSubCommand("Company", "Invite another player to your company.", ChatAuthorizationLevel.User)]
        public static void Invite(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't send company invite as you are not a CEO of any company");
                return;
            }
            if (user != company.Ceo)
            {
                user.Player?.OkBoxLoc($"Couldn't send company invite as you are not the CEO of {company.MarkedUpName}");
                return;
            }
            company.TryInvite(user.Player, otherUser);
        }

        [ChatSubCommand("Company", "Withdraws an invitation for another player to your company.", ChatAuthorizationLevel.User)]
        public static void Uninvite(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't withdraw company invite as you are not a CEO of any company");
                return;
            }
            if (user != company.Ceo)
            {
                user.Player?.OkBoxLoc($"Couldn't withdraw company invite as you are not the CEO of {company.MarkedUpName}");
                return;
            }
            company.TryUninvite(user.Player, otherUser);
        }

        [ChatSubCommand("Company", "Removes an employee from your company.", ChatAuthorizationLevel.User)]
        public static void Fire(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't fire employee as you are not a CEO of any company");
                return;
            }
            if (user != company.Ceo)
            {
                user.Player?.OkBoxLoc($"Couldn't fire employee as you are not the CEO of {company.MarkedUpName}");
                return;
            }
            company.TryFire(user.Player, otherUser);
        }

        [ChatSubCommand("Company", "Accepts an invite to join a company.", ChatAuthorizationLevel.User)]
        public static void Join(User user, Company company)
        {
            company.TryJoin(user.Player, user);
        }

        [ChatSubCommand("Company", "Resigns you from your current company.", ChatAuthorizationLevel.User)]
        public static void Leave(User user)
        {
            var currentEmployer = Companies.Company.GetEmployer(user);
            if (currentEmployer == null)
            {
                user.Player?.OkBoxLoc($"Couldn't resign from your company as you're not currently employed");
                return;
            }
            currentEmployer.TryLeave(user.Player, user);
        }

        [ChatSubCommand("Company", "Edits the company owned deed that you're currently standing in.", ChatAuthorizationLevel.User)]
        public static void EditDeed(User user)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as you're not currently employed");
                return;
            }
            var deed = PropertyManager.GetDeedWorldPos(new Vector2i((int)user.Position.X, (int)user.Position.Z));
            if (deed == null)
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as you're not standing on one");
                return;
            }
            if (!company.OwnedDeeds.Contains(deed))
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as it's not owned by {company.MarkedUpName}");
                return;
            }
            DeedEditingUtil.EditInMap(deed, user);
        }
    }
}