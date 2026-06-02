// SPDX-FileCopyrightText: 2023 ElectroJr <leonsfriedrich@gmail.com>
// SPDX-FileCopyrightText: 2023 Emisse <99158783+Emisse@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 TemporalOroboros <TemporalOroboros@gmail.com>
// SPDX-FileCopyrightText: 2023 iller_saver <55444968+illersaver@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 themias <89101928+themias@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Jezithyr <jezithyr@gmail.com>
// SPDX-FileCopyrightText: 2024 leonidussaks <42278348+leonidussaks@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 nikthechampiongr <32041239+nikthechampiongr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aviu00 <93730715+Aviu00@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2025 ScarKy0 <106310278+ScarKy0@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.DoAfter;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Nutrition.Components;
using Content.Server.Popups;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.EntitySystems;

/// <summary>
/// System for vapes
/// </summary>
namespace Content.Server.Nutrition.EntitySystems
{
    public sealed partial class SmokingSystem
    {
        [Dependency] private readonly DoAfterSystem _doAfterSystem = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly EmagSystem _emag = default!;
        [Dependency] private readonly IngestionSystem _ingestion = default!;
        [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;

        private void InitializeVapes()
        {
            SubscribeLocalEvent<VapeComponent, AfterInteractEvent>(OnVapeInteraction);
            SubscribeLocalEvent<VapeComponent, VapeDoAfterEvent>(OnVapeDoAfter);
            SubscribeLocalEvent<VapeComponent, GotEmaggedEvent>(OnEmagged);
        }

    ///оффы накакали, и система обращалась хуй пойми к чему. Писюн больше не чешется - я его почесал @axilmon
        private void OnVapeInteraction(EntityUid uid, VapeComponent comp, ref AfterInteractEvent args)
        {
            var delay = comp.Delay;
            var forced = true;
            var exploded = false;

            if (!args.CanReach
                || !_solutionContainerSystem.TryGetRefillableSolution(uid, out _, out var solution) //arcane-station
                || args.Target == null //arcane-station
                || !HasComp<BloodstreamComponent>(args.Target.Value) //arcane-station
                || (args.Target != args.User && _ingestion.HasMouthAvailable(args.Target.Value, args.User)) //arcane-station
                )
            {
                return;
            }

            if (solution.Contents.Count == 0)
            {
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-empty"), args.Target.Value,
                    args.User);
                return;
            }

            if (args.Target == args.User)
            {
                delay = comp.UserDelay;
                forced = false;
            }

            if (comp.ExplodeOnUse || _emag.CheckFlag(uid, EmagType.Interaction))
            {
                _explosionSystem.QueueExplosion(uid, "Default", comp.ExplosionIntensity, 0.5f, 3, canCreateVacuum: false);
                Del(uid);
                exploded = true;
            }
            else
            {
                foreach (var name in solution.Contents)
                {
                    if (name.Reagent.Prototype != comp.SolutionNeeded)
                    {
                        exploded = true;
                        _explosionSystem.QueueExplosion(uid, "Default", comp.ExplosionIntensity, 0.5f, 3, canCreateVacuum: false);
                        Del(uid);
                        break;
                    }
                }
            }

            if (forced)
            {
                var targetName = Identity.Entity(args.Target.Value, EntityManager);
                var userName = Identity.Entity(args.User, EntityManager);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape-forced", ("user", userName)), args.Target.Value,
                    args.Target.Value);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape-forced-user", ("target", targetName)), args.User,
                    args.User);
            }
            else
            {
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape"), args.User,
                    args.User);
            }

            if (!exploded)
            {
                var vapeDoAfterEvent = new VapeDoAfterEvent(solution, forced);
                _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, delay, vapeDoAfterEvent, uid, target: args.Target, used: uid)
                {
                    BreakOnMove = false,
                    BreakOnDamage = true,
                    MultiplyDelay = false, // Goobstation
                });
            }
            args.Handled = true;
        }

        private void OnVapeDoAfter(EntityUid uid, VapeComponent comp, ref VapeDoAfterEvent args)
        {
            if (args.Cancelled || args.Handled || args.Args.Target == null)
                return;

            var environment = _atmos.GetContainingMixture(args.Args.Target.Value, true, true);
            if (environment == null)
            {
                return;
            }

            //Smoking kills(your lungs, but there is no organ damage yet)
            _damageableSystem.TryChangeDamage(args.Args.Target.Value, comp.Damage, true);

            var merger = new GasMixture(1) { Temperature = args.Solution.Temperature };
            merger.SetMoles(comp.GasType, args.Solution.Volume.Value / comp.ReductionFactor);

            _atmos.Merge(environment, merger);

            args.Solution.RemoveAllSolution();

            if (args.Forced)
            {
                var targetName = Identity.Entity(args.Args.Target.Value, EntityManager);
                var userName = Identity.Entity(args.Args.User, EntityManager);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success-forced", ("user", userName)), args.Args.Target.Value,
                    args.Args.Target.Value);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success-user-forced", ("target", targetName)), args.Args.User,
                    args.Args.Target.Value);
            }
            else
            {
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success"), args.Args.Target.Value,
                    args.Args.Target.Value);
            }
        }

        private void OnEmagged(EntityUid uid, VapeComponent comp, ref GotEmaggedEvent args)
        {
            if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
                return;

            if (_emag.CheckFlag(uid, EmagType.Interaction))
                return;

            args.Handled = true;
        }
    }
}
