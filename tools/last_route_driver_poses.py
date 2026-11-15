"""Authored Ferryman beats. The cabin builder fits these takes to the default rig and sedan."""
from __future__ import annotations


def action_keys(rig_tools):
    BonePose = rig_tools.BonePose
    merge_pose = rig_tools.merge_pose
    interpolate_pose = rig_tools.interpolate_pose

    def ferryman_base_pose() -> dict[str, BonePose]:
        """Perched on the car's bonnet, braced back on one hand, coin ready.

        The whole design is in this pose, so it is worth saying what each
        half is for. The RIGHT arm is furniture: it goes back and down onto
        the bonnet behind his hip and never moves again in the loop. The
        LEFT arm is the gesture: forearm up, palm open at chest height,
        and it is the only thing that moves apart from his breathing. One
        arm fixed and one arm working is what makes a toss legible from
        across the lot, where a two-handed fidget would read as noise.

        The lean is backwards, not forwards. A man bowed over his knees
        reads as tired; a man tipped back on his supporting hand reads as
        someone enjoying the wait, which is the whole character. It is a
        small lean, though - see the pelvis below for why it stopped being
        a large one. The head is turned very slightly off the coin so that
        from the approach he looks like he is watching the arriving hero
        instead - the shadow under the cap brim does the rest.

        The legs are the measured half, exactly as they are for the chess
        player. His boots rest on the car's front bumper, 0.505 m below the
        bonnet skin he sits on and 0.42 m ahead of it - both numbers come
        from PERCH_SEAT and PERCH_SOLES in
        tools/build-last-route-car-3d-model.py, and `perch_seat_height_m`
        is what pins the drop.
        """

        return {
            # Tipped back off the hips, chest open - but only just. The first
            # pass leaned him twelve degrees off vertical with his chin up to
            # match, and at the size he is drawn on screen that read as a man
            # staring at the sky rather than at whoever is walking up. Four
            # degrees came out of the pelvis and the spine and four out of the
            # neck and the head, which leaves the lean but points the face
            # along the ground. Anything that moves these has to re-converge
            # the two things they carry: the thighs below (which hold his
            # boots on the bumper) and the bracing arm behind (which holds his
            # hand on the bonnet).
            "pelvis": BonePose(rotation_degrees=(-3.0, 0.0, 0.0), location_m=(0, 0.010, -0.004)),
            "spine": BonePose(rotation_degrees=(-2.0, 2.0, 0.0)),
            "chest": BonePose(rotation_degrees=(-2.0, 3.0, 0.0)),
            # Chin level and turned a few degrees toward whoever is walking
            # up, so the cap brim's shadow still covers the eyes.
            "neck": BonePose(rotation_degrees=(0.0, -7.0, 0.0)),
            "head": BonePose(rotation_degrees=(-2.0, -6.0, 2.0)),
            "clavicle.L": BonePose(rotation_degrees=(2.0, -4.0, 6.0)),
            "clavicle.R": BonePose(rotation_degrees=(-4.0, 4.0, -10.0)),
            # Left: the coin hand. Elbow in at the ribs, forearm up, palm
            # open and level at chest height where the toss starts.
            "upper_arm.L": BonePose(rotation_degrees=(-150.0, 4.0, 74.0)),
            "forearm.L": BonePose(rotation_degrees=(-78.0, 40.0, 8.0)),
            "hand.L": BonePose(rotation_degrees=(10.0, 20.0, -10.0)),
            # Right: the brace. These three are a MEASURED result, not a
            # posed one. Nothing about this rig's shoulder is guessable -
            # raising |X| lifts the hand rather than lowering it, and Z
            # swings it across the body - so the angles were swept against
            # one target taken off the car: the heel of his hand on the
            # bonnet behind his hip. Re-swept after the torso was levelled,
            # because sitting up moves the shoulder and takes the hand with
            # it: they now land the underside of his palm 0.317 m behind him
            # and 3 mm over the bonnet skin. Re-posing by eye will move it
            # off the metal and he will be bracing on air.
            "upper_arm.R": BonePose(rotation_degrees=(-20.0, -10.0, -62.0)),
            "forearm.R": BonePose(rotation_degrees=(-8.0, -6.0, -4.0)),
            "hand.R": BonePose(rotation_degrees=(-16.0, -6.0, 6.0)),
            # Both boots rest on the bumper, and that is a contract rather
            # than a look: the wait loop swings one leg at a time (see
            # `ferry_kick`), so the OTHER boot is what the perch measurement
            # reads every frame. They were levelled to within a millimetre of
            # each other for exactly that reason - a leg left hanging seven
            # centimetres high, as the first pass had it, moves the measured
            # seat by seven centimetres the moment its partner kicks.
            #
            # Solved, not posed: swept against the car's own 0.505 m from
            # bonnet skin to bumper top and landing in the 0.5060-0.5077 band.
            # Thigh angle is what moves this number; bending the shin further barely
            # touches it, which is not obvious and cost a sweep to learn.
            # The left thigh is the shallower of the two because the pelvis
            # above it is turned, not because the legs are meant to differ.
            "thigh.L": BonePose(rotation_degrees=(-71.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(42.0, 0.0, 0.0)),
            "foot.L": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
            "thigh.R": BonePose(rotation_degrees=(-72.0, 0.0, -8.0)),
            "shin.R": BonePose(rotation_degrees=(38.0, 0.0, 0.0)),
            "foot.R": BonePose(rotation_degrees=(6.0, 0.0, -6.0)),
        }


    def ferryman_stand_pose() -> dict[str, BonePose]:
        """The standing stance his walk is built on, for the trudge slot."""

        return {
            "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
            "spine": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
            "chest": BonePose(rotation_degrees=(3.0, 0.0, 0.0)),
            "neck": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
            "head": BonePose(rotation_degrees=(-2.0, 0.0, 0.0)),
            "clavicle.L": BonePose(rotation_degrees=(2.0, -3.0, 5.0)),
            "clavicle.R": BonePose(rotation_degrees=(2.0, 3.0, -5.0)),
            "upper_arm.L": BonePose(rotation_degrees=(-8.0, 0.0, 62.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-8.0, 0.0, -62.0)),
            "forearm.L": BonePose(rotation_degrees=(-16.0, 10.0, 4.0)),
            "forearm.R": BonePose(rotation_degrees=(-16.0, -10.0, -4.0)),
            "hand.L": BonePose(rotation_degrees=(4.0, 6.0, -4.0)),
            "hand.R": BonePose(rotation_degrees=(4.0, -6.0, 4.0)),
        }


    def ferryman_drive_pose() -> dict[str, BonePose]:
        """Behind the wheel of his own car, waiting again.

        A cabin seat, not a bonnet, and a small car's cabin rather than the
        bus's: both halves of this pose are measured against
        tools/build-last-route-car-3d-model.py rather than eyeballed, and
        both were wrong when they were.

        Under him there is far less room than a bus gives. FLOOR_Z 0.30 sits
        only 0.22 m below SEAT_PELVIS_Z 0.52, where Route 01 allows 0.41, so
        a passenger's hanging shins put his boots a fifth of a metre through
        the floor pan. A driver does not hang his legs anyway - he reaches
        them forward onto the pedals - and that is what the angles below do:
        the thighs run slightly above level and the shins slope forward, for
        0.2197 m of leg under the pelvis against the 0.22 m there is.

        Over him there is less room too. ROOF_UNDERSIDE_Z 1.56 leaves 1.04 m
        of head, and sitting up straight he measures 1.0430 m to the crown of
        his cap - three millimetres of roof, worn. So he sits into the seat
        rather than on it, which is what a man who has waited twenty years
        does regardless, and clears it by twelve.

        Both hands come onto the rim - the bracing arm has nothing to brace
        on any more, and a driver with one hand down reads as parked rather
        than as about to leave.
        """

        return {
            "pelvis": BonePose(rotation_degrees=(4.0, 0.0, 0.0)),
            "spine": BonePose(rotation_degrees=(14.0, 0.0, 0.0)),
            "chest": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
            "neck": BonePose(rotation_degrees=(2.0, 0.0, 0.0)),
            "head": BonePose(rotation_degrees=(-3.0, 0.0, 0.0)),
            "clavicle.L": BonePose(rotation_degrees=(3.0, -3.0, 6.0)),
            "clavicle.R": BonePose(rotation_degrees=(3.0, 3.0, -6.0)),
            # Both forearms up and in, hands meeting on the rim in front of
            # the chest. Mirrored exactly: a wheel is symmetric and so is
            # the grip on it.
            "upper_arm.L": BonePose(rotation_degrees=(-148.0, 4.0, 72.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-148.0, -4.0, -72.0)),
            "forearm.L": BonePose(rotation_degrees=(-86.0, 42.0, 8.0)),
            "forearm.R": BonePose(rotation_degrees=(-86.0, -42.0, -8.0)),
            "hand.L": BonePose(rotation_degrees=(8.0, 24.0, -14.0)),
            "hand.R": BonePose(rotation_degrees=(8.0, -24.0, 14.0)),
            # Driving legs: thighs a little above level and the shins reaching
            # forward at the pedals, not hanging. Converged against the car's
            # 0.22 m of floor rather than chosen - see the docstring.
            "thigh.L": BonePose(rotation_degrees=(-90.3, 0.0, 4.0)),
            "shin.L": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
            "foot.L": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
            "thigh.R": BonePose(rotation_degrees=(-90.3, 0.0, -4.0)),
            "shin.R": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
            "foot.R": BonePose(rotation_degrees=(-6.0, 0.0, 0.0)),
        }


    def ferry_breath(base: dict[str, BonePose], amount: float) -> dict[str, BonePose]:
        """One breath carried by the ribs, the fisherman's own idiom.

        The chest lifts and the shoulders follow it a little; nothing else
        moves. On a man sitting still this is the only thing that keeps the
        loop from reading as a freeze frame.
        """

        return merge_pose(
            base,
            {
                "chest": BonePose(
                    rotation_degrees=(-2.0 - 1.8 * amount, 3.0, 0.0)
                ),
                "clavicle.L": BonePose(
                    rotation_degrees=(2.0 + 1.4 * amount, -4.0, 6.0)
                ),
                "clavicle.R": BonePose(
                    rotation_degrees=(-4.0 + 1.4 * amount, 4.0, -10.0)
                ),
            },
        )


    def ferry_kick(
        base: dict[str, BonePose], side: str, amount: float
    ) -> dict[str, BonePose]:
        """One idle swing of one leg, off the bumper and back onto it.

        A man who has waited twenty years on the nose of his own car does
        not sit still, and this is the cheapest thing he can be doing that
        is not the coin: he knocks a heel off the bumper and lets it drop
        back. Twenty degrees out of the knee lifts the boot about six
        centimetres, which is small in metres and unmistakable in motion.

        ONE LEG AT A TIME, and that is a contract rather than a preference.
        The perch validator measures his seat against the lowest drawn point
        of the whole model every frame, and on this design that point is a
        boot sole. So the loop is authored to keep the other boot on the
        metal throughout - both are levelled to the millimetre in the base
        pose for exactly this - and the measurement never notices that
        anything moved. Two legs up at once would swing the seat by the full
        amplitude and read as a man levitating off his own bonnet.

        Every angle is read off the base pose rather than re-typed, so
        re-converging the legs against the car moves the kick with them.
        """

        swings = (
            (f"thigh.{side}", -3.0),
            (f"shin.{side}", -20.0),
            (f"foot.{side}", -6.0),
        )
        kicked: dict[str, BonePose] = {}
        for bone_name, degrees in swings:
            rest = base[bone_name]
            pitch, yaw, roll = rest.rotation_degrees
            kicked[bone_name] = BonePose(
                rotation_degrees=(pitch + degrees * amount, yaw, roll),
                location_m=rest.location_m,
            )
        return kicked


    # --- The ordinary citizen gait -------------------------------------------
    #
    # THIS IS THE HERO'S OWN WALK, TRANSPLANTED. `tools/player_3d_model_common.py`
    # authors `Walk` as an eight-key cycle - contact, down, pass, up for each leg
    # - over `1.0 s` at `24` fps, and every leg, pelvis, spine, chest, head, arm
    # and hand key below is copied from it verbatim. It transfers without
    # retargeting because both rigs are the same 31-bone NpcHumanV2 skeleton with
    # the same rest pose; `npc_v2_bone_specs()` reproduces Hero V2's landmarks to
    # the millimetre.
    #
    # THE ARMS ARE HIS AIM VECTORS, NOT ROTATIONS. The hero points each upper arm
    # at an armature-space `target_direction` - a hanging arm is `(0.06, 0, -0.33)`,
    # straight down and a hand's width out - and keys the forearm and hand as
    # plain bone-local rotations under it. Until 2026-09-05 this recipe
    # re-expressed those aims as X rotations "of the same magnitude" and merged
    # them OVER each design's base arms. On the shared A-pose rig the upper arm's
    # local X axis points back and up, not sideways, so an X turn is abduction,
    # not a forward swing: the base's hang was thrown away, every street copy
    # stood in the bind A-pose and flapped +-25 deg like a bird, and the street
    # idle - `1.2 deg` of X on the bare rest - was the A-pose outright. That is
    # what the user saw as arms held out sideways and a strange walk.
    # `BonePose.target_direction` now exists here and `apply_pose` solves it the
    # way his generator does, so the vectors are carried over unchanged.
    #
    # The cycle is a RECIPE rather than a clip: it is merged onto each design's
    # own STANDING base pose, and it owns more of it than the arms. The walk keys
    # pelvis, spine, chest and head from the hero's cycle and the idle's breath
    # zeroes spine, chest and head, so on the street a design keeps only its NECK
    # - and, in the idle, its pelvis (pitch and location offset) and legs. An
    # authored stoop in spine/chest/head never reaches the street clips; tune a
    # crossing stance through the neck and pelvis, or through this recipe. The
    # base's clavicles are reset too - the hang is measured from a rest shoulder,
    # and a design's own shoulder shaping (a hunch, a shrug) would tilt it.

    # The hero's relaxed arms: `relaxed_pose()` in his generator, verbatim.
    CITIZEN_HANGING_ARMS: dict[str, BonePose] = {
        "clavicle.L": BonePose(),
        "clavicle.R": BonePose(),
        "upper_arm.L": BonePose(target_direction=(0.059, -0.014, -0.334)),
        "upper_arm.R": BonePose(target_direction=(-0.047, -0.010, -0.334)),
        "forearm.L": BonePose(rotation_degrees=(-4.0, 3.0, -2.0)),
        "forearm.R": BonePose(rotation_degrees=(-5.0, -3.0, 2.0)),
        "hand.L": BonePose(rotation_degrees=(2.0, -5.0, 2.0)),
        "hand.R": BonePose(rotation_degrees=(2.0, 5.0, -2.0)),
    }

    # The bones the citizen recipe owns on every design it is merged onto.
    CITIZEN_ARM_BONES = tuple(CITIZEN_HANGING_ARMS)


    # ------------------------------------------------------- ferryman
    ferry_wait = ferryman_base_pose()
    ferry_wait_inhale = ferry_breath(ferry_wait, 1.0)
    # The throw, and it is a throw now.
    #
    # It used to live entirely in the wrist: fourteen degrees of elbow,
    # thirty-six of hand, and the shoulder and clavicle not keyed at all.
    # The reasoning was that a big swing would read as a man throwing
    # something away rather than one amusing himself, and at the size he
    # is drawn that bought nothing - the coin moved and the man did not,
    # so it read as a chip of light jumping beside a statue. It is now
    # the whole arm: down to the hip, up on a nearly straight one, and
    # giving on the catch. The hand travels 0.78 m instead of 0.06 m.
    #
    # Every angle below is MEASURED off the rig rather than posed by eye.
    # The rest pose is a T, the eulers are bone-local, and none of this is
    # derivable from the numbers already in this file: on `upper_arm.L`,
    # MORE NEGATIVE X raises the hand (-90 puts it at his hip, -195 half
    # a metre above his shoulder) and +Z carries it out away from the
    # ribs, which is what stops the raised arm folding back beside his cap
    # like a salute. Sweep it again before changing it.
    #
    # `chest`, `spine` and `pelvis` are deliberately absent. His RIGHT
    # palm braces on the bonnet with three millimetres to spare and hangs
    # off `clavicle.R` on the chest, so turning the torso into the throw
    # would take that hand off the metal. The whole body of the gesture
    # goes through `clavicle.L`, which is on the free side.
    def ferry_toss(
        base: dict[str, BonePose],
        clavicle: tuple[float, float, float],
        upper_arm: tuple[float, float, float],
        forearm: tuple[float, float, float],
        hand: tuple[float, float, float],
        head: tuple[float, float, float],
    ) -> dict[str, BonePose]:
        return merge_pose(
            base,
            {
                "clavicle.L": BonePose(rotation_degrees=clavicle),
                "upper_arm.L": BonePose(rotation_degrees=upper_arm),
                "forearm.L": BonePose(rotation_degrees=forearm),
                "hand.L": BonePose(rotation_degrees=hand),
                "head": BonePose(rotation_degrees=head),
            },
        )

    # Wind-up: the arm swings down and the open palm carries the coin to
    # his hip. The coin is not airborne until the next key, so it rides
    # the hand all the way down.
    ferry_wait_windup = ferry_toss(
        ferry_wait,
        (8.0, -4.0, 14.0), (-90.0, 4.0, 66.0), (-34.0, 40.0, 8.0),
        (28.0, 20.0, -10.0), (4.0, -6.0, 2.0),
    )
    # Release, on the way up and moving. The wrist snaps through here;
    # everything after it is the arm following the coin.
    ferry_wait_flick = ferry_toss(
        ferry_wait,
        (4.0, -4.0, 10.0), (-116.0, 4.0, 74.0), (-54.0, 40.0, 8.0),
        (-34.0, 20.0, -10.0), (-2.0, -6.0, 2.0),
    )
    # Still rising, with the coin above him. This key used to be the
    # plain inhale, which is built off the BASE pose - so the forearm and
    # the wrist snapped back to rest in mid-flight, within a frame or two
    # of the coin reaching the top of its arc.
    ferry_wait_lift = ferry_toss(
        ferry_wait_inhale,
        (-2.0, -4.0, 2.0), (-158.0, 4.0, 88.0), (-62.0, 40.0, 8.0),
        (-10.0, 20.0, -10.0), (-9.0, -6.0, 2.0),
    )
    # Full extension, waiting for it: half a metre above his own shoulder
    # on an almost straight arm, and clear of the cap by 0.38 m.
    ferry_wait_reach = ferry_toss(
        ferry_wait,
        (-6.0, -4.0, -2.0), (-195.0, 4.0, 94.0), (-45.0, 40.0, 8.0),
        (6.0, 20.0, -10.0), (-15.0, -5.0, 2.0),
    )
    # And gives with the catch, the way a caught weight is absorbed.
    ferry_wait_catch = ferry_toss(
        ferry_wait,
        (-4.0, -4.0, 0.0), (-176.0, 4.0, 92.0), (-58.0, 40.0, 8.0),
        (32.0, 20.0, -10.0), (-6.0, -7.0, 2.0),
    )
    # The arm coming back down to where it rests. Formerly a plain inhale
    # too, and for the same reason it could not stay one: it would snap
    # the whole arm home inside a single frame.
    ferry_wait_settle = ferry_toss(
        ferry_wait_inhale,
        (2.0, -4.0, 6.0), (-160.0, 4.0, 80.0), (-84.0, 40.0, 8.0),
        (16.0, 20.0, -10.0), (-2.0, -6.0, 2.0),
    )
    # Once a loop he looks up from the coin at the island instead.
    ferry_wait_glance = merge_pose(
        ferry_wait,
        {
            "neck": BonePose(rotation_degrees=(-2.0, -14.0, 0.0)),
            "head": BonePose(rotation_degrees=(0.0, -12.0, 3.0)),
        },
    )

    ferry_stand = ferryman_stand_pose()
    ferry_step_l = merge_pose(
        ferry_stand,
        {
            "thigh.L": BonePose(rotation_degrees=(-20.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
            "thigh.R": BonePose(rotation_degrees=(14.0, 0.0, -3.0)),
            "shin.R": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
            "upper_arm.L": BonePose(rotation_degrees=(-14.0, 0.0, 62.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-2.0, 0.0, -62.0)),
        },
    )
    ferry_step_r = merge_pose(
        ferry_stand,
        {
            "thigh.R": BonePose(rotation_degrees=(-20.0, 0.0, -3.0)),
            "shin.R": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
            "thigh.L": BonePose(rotation_degrees=(14.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-14.0, 0.0, -62.0)),
            "upper_arm.L": BonePose(rotation_degrees=(-2.0, 0.0, 62.0)),
        },
    )
    ferry_pass_r = merge_pose(
        ferry_stand,
        {
            "thigh.R": BonePose(rotation_degrees=(-6.0, 0.0, -3.0)),
            "shin.R": BonePose(rotation_degrees=(26.0, 0.0, 0.0)),
            "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0), location_m=(0, 0, 0.008)),
        },
    )
    ferry_pass_l = merge_pose(
        ferry_stand,
        {
            "thigh.L": BonePose(rotation_degrees=(-6.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(26.0, 0.0, 0.0)),
            "pelvis": BonePose(rotation_degrees=(2.0, 0.0, 0.0), location_m=(0, 0, 0.008)),
        },
    )

    ferry_drive = ferryman_drive_pose()
    ferry_drive_breath = merge_pose(
        ferry_drive,
        {
            "chest": BonePose(rotation_degrees=(1.5, 0.0, 0.0)),
            "clavicle.L": BonePose(rotation_degrees=(3.0, -3.0, 6.0)),
            "clavicle.R": BonePose(rotation_degrees=(3.0, 3.0, -6.0)),
        },
    )
    # The push off the metal: the bracing arm straightens hard and the
    # hips leave the bonnet before anything else has moved. This used to
    # open the board transition, back when getting off the car and getting
    # into it were one three-quarter-second clip; it now opens the DROP,
    # which is the only half of that it was ever really about.
    ferry_drop_push = merge_pose(
        ferry_wait,
        {
            "pelvis": BonePose(rotation_degrees=(-2.0, 0.0, 0.0), location_m=(0, -0.04, 0.06)),
            "spine": BonePose(rotation_degrees=(6.0, 4.0, 0.0)),
            "upper_arm.R": BonePose(rotation_degrees=(-6.0, -10.0, -76.0)),
            "thigh.L": BonePose(rotation_degrees=(-32.0, 0.0, 3.0)),
            "thigh.R": BonePose(rotation_degrees=(-28.0, 0.0, -8.0)),
            "head": BonePose(rotation_degrees=(-2.0, -18.0, 2.0)),
        },
    )
    # In the air. Sampled between the two authored postures rather than
    # posed from scratch: both ends of this drop are converged shapes -
    # the perch against the car's bumper, the stance against the ground -
    # and anything hand-written in between would drift off the line they
    # already describe. Only the legs and the bow get their own numbers,
    # because they are what the drop is; the arms coming down off the
    # brace are exactly the interpolation.
    ferry_drop_air = interpolate_pose(ferry_wait, ferry_stand, 0.55)
    # Boots down, knees taking it, head over the landing. Measured rather
    # than eyeballed: this puts his soles 16 mm over the plane his stand
    # pose sits on, which is a landing rather than a stamp, and the next
    # two keys close that to nothing.
    ferry_drop_land = merge_pose(
        interpolate_pose(ferry_wait, ferry_stand, 0.90),
        {
            "spine": BonePose(rotation_degrees=(11.0, 0.0, 0.0)),
            "chest": BonePose(rotation_degrees=(6.0, 0.0, 0.0)),
            "head": BonePose(rotation_degrees=(7.0, 0.0, 0.0)),
            "thigh.L": BonePose(rotation_degrees=(-26.0, 0.0, 3.0)),
            "shin.L": BonePose(rotation_degrees=(30.0, 0.0, 0.0)),
            "foot.L": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
            "thigh.R": BonePose(rotation_degrees=(-24.0, 0.0, -3.0)),
            "shin.R": BonePose(rotation_degrees=(28.0, 0.0, 0.0)),
            "foot.R": BonePose(rotation_degrees=(-4.0, 0.0, 0.0)),
        },
    )
    ferry_drop_rise = interpolate_pose(ferry_drop_land, ferry_stand, 0.60)

    # ---------------------------------------------------- the door
    # He takes the handle with his LEFT hand, and that is geometry rather
    # than preference: the car is left-hand drive, so with the rig facing
    # its own -Y the driver's door is on his left in the cabin and dead
    # ahead of him on the way in.
    #
    # All three arm shapes below are SOLVED, not posed. This rig's
    # shoulder Eulers are not guessable - the same trap the bracing arm
    # of the perch fell into - so each one came out of a sweep against a
    # hand target: 0.43 m in front of him at the car's waist line for the
    # handle, drawn back and across for the pull, and 0.63 m out to his
    # left for the door edge once he is sitting down. Re-pose any of them
    # by eye and he mimes at air.
    ferry_door_reach = merge_pose(
        ferry_stand,
        {
            "spine": BonePose(rotation_degrees=(12.0, 6.0, 0.0)),
            "chest": BonePose(rotation_degrees=(6.0, 8.0, 0.0)),
            "upper_arm.L": BonePose(rotation_degrees=(-90.0, 0.0, 60.0)),
            "forearm.L": BonePose(rotation_degrees=(-60.0, 0.0, 0.0)),
            "hand.L": BonePose(rotation_degrees=(0.0, 0.0, 0.0)),
        },
    )
    ferry_door_pull = merge_pose(
        ferry_stand,
        {
            "spine": BonePose(rotation_degrees=(4.0, -8.0, 0.0)),
            "chest": BonePose(rotation_degrees=(2.0, -10.0, 0.0)),
            "upper_arm.L": BonePose(rotation_degrees=(-20.0, -10.0, 20.0)),
            "forearm.L": BonePose(rotation_degrees=(-80.0, 0.0, 0.0)),
            "hand.L": BonePose(rotation_degrees=(0.0, 0.0, 0.0)),
            # Weight going back onto the rear foot as the door comes.
            "thigh.L": BonePose(rotation_degrees=(10.0, 0.0, 3.0)),
            "thigh.R": BonePose(rotation_degrees=(-8.0, 0.0, -3.0)),
        },
    )
    ferry_door_clear = merge_pose(
        ferry_stand,
        {
            "spine": BonePose(rotation_degrees=(2.0, -6.0, 0.0)),
            "head": BonePose(rotation_degrees=(0.0, -8.0, 0.0)),
        },
    )
    # Getting in. The three sampled amounts track the ROOT's own travel
    # into the seat in LastRouteFerrymanBoardingTimeline - 0.22, 0.70 and
    # 0.97 at the keys below - so the pose and the metres arrive together
    # instead of him sitting down in mid-air and sliding in afterwards.
    ferry_seat_step = merge_pose(
        interpolate_pose(ferry_stand, ferry_drive, 0.22),
        {
            "spine": BonePose(rotation_degrees=(24.0, 0.0, 0.0)),
            "chest": BonePose(rotation_degrees=(10.0, 0.0, 0.0)),
            "head": BonePose(rotation_degrees=(16.0, 0.0, 0.0)),
        },
    )
    ferry_seat_settle = merge_pose(
        interpolate_pose(ferry_stand, ferry_drive, 0.70),
        {
            "spine": BonePose(rotation_degrees=(18.0, 0.0, 0.0)),
            "head": BonePose(rotation_degrees=(8.0, 0.0, 0.0)),
        },
    )
    ferry_seat_down = interpolate_pose(ferry_stand, ferry_drive, 0.97)
    ferry_door_shut = merge_pose(
        ferry_drive,
        {
            "spine": BonePose(rotation_degrees=(10.0, -14.0, 0.0)),
            "chest": BonePose(rotation_degrees=(4.0, -16.0, 0.0)),
            "upper_arm.L": BonePose(rotation_degrees=(-20.0, 0.0, 30.0)),
            "forearm.L": BonePose(rotation_degrees=(20.0, 0.0, 0.0)),
            "hand.L": BonePose(rotation_degrees=(0.0, 0.0, 0.0)),
        },
    )

    return {
        # The Ferryman's wait. A quarter-loop breath grid, exactly the
        # fisherman's contract, plus the one thing he does. The coin
        # leaves his palm at 0.0625 and lands back in it at 0.3125; the
        # hand rises through the throw and drops to meet the fall,
        # because a hand that stays still while a coin arcs off it reads
        # as the coin being fired rather than flicked.
        #
        # He also swings his legs, and the three kicks below are placed
        # on keys whose NEIGHBOURS leave that leg at rest, so each kick
        # is one clean rise and fall and the other boot never leaves the
        # bumper. Right, left, and a half-hearted right on the way out:
        # unevenly spaced on purpose, because a man idling his feet is
        # not a metronome.
        # The release and the catch keys are a CONTRACT with
        # `LastRouteFerrymanPresentation.TossReleasePhase` and
        # `TossCatchPhase` (1/16 and 5/16), which is where the runtime
        # hands the coin over to its own arc and takes it back. Those two
        # times may not move. The wind-up before the release and the three
        # keys carrying the arm up and back down between them are free,
        # and the throw needs all of them: the arm covers 0.78 m, and on
        # the old four-key grid it did that in two frames and snapped back
        # to rest in the middle of the coin's flight.
        "FerrymanWait": (
            (0.0, ferry_wait),
            (1.0 / 24.0, ferry_wait_windup),
            (0.0625, ferry_wait_flick),
            (0.125, ferry_wait_lift),
            (0.25, merge_pose(
                ferry_wait_reach, ferry_kick(ferry_wait, "R", 1.0))),
            (0.3125, ferry_wait_catch),
            (0.375, ferry_wait_settle),
            (0.5, ferry_wait),
            (0.625, merge_pose(
                ferry_wait_inhale, ferry_kick(ferry_wait, "L", 1.0))),
            (0.75, ferry_wait_glance),
            (0.875, merge_pose(
                ferry_wait_inhale, ferry_kick(ferry_wait, "R", 0.5))),
            (1.0, ferry_wait),
        ),
        "FerrymanTrudge": (
            (0.0, ferry_step_l),
            (0.25, ferry_pass_r),
            (0.5, ferry_step_r),
            (0.75, ferry_pass_l),
            (1.0, ferry_step_l),
        ),
        # Off the bonnet and onto the lot. One second, and the landing key
        # at 0.62 is a CONTRACT: the runtime drops his root on the same
        # fraction and kicks the car's springs there, so a man whose boots
        # touch at 0.5 would rock a car he is still falling past. Opens on
        # the exact base pose of FerrymanWait and closes on the exact base
        # pose of FerrymanTrudge, so the wait crosses in and the walk
        # crosses out with nothing to blend at either end.
        "FerrymanDismount": (
            (0.0, ferry_wait),
            (0.18, ferry_drop_push),
            (0.42, ferry_drop_air),
            (0.62, ferry_drop_land),
            (0.82, ferry_drop_rise),
            (1.0, ferry_stand),
        ),
        # Standing at his own door, and then behind his own wheel. Four
        # of these keys are contracts with
        # LastRouteFerrymanBoardingTimeline rather than free choices: the
        # leaf starts moving at 0.16 and is open at 0.34, the root leaves
        # the standing point at 0.36 and arrives at 0.78, and the leaf is
        # pulled shut between 0.84 and 0.98. Re-time the grid without
        # re-timing those and he walks through his own door.
        "FerrymanBoard": (
            (0.0, ferry_stand),
            (0.10, ferry_door_reach),
            (0.22, ferry_door_pull),
            (0.34, ferry_door_clear),
            (0.48, ferry_seat_step),
            (0.62, ferry_seat_settle),
            (0.74, ferry_seat_down),
            (0.88, ferry_door_shut),
            (1.0, ferry_drive),
        ),
        "FerrymanDrive": (
            (0.0, ferry_drive),
            (0.5, ferry_drive_breath),
            (1.0, ferry_drive),
        ),
    }
