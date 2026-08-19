# System tree

## Current repository

```text
Assets/
  Scenes/
    MainMenu.unity
    City.unity
    DoorTransition.unity
    BarInterior.unity
    SupermarketInterior.unity
    HomeInterior.unity
    StairwellInterior.unity
  Settings/
    CityNoirVolumeProfile.asset
    PC_Renderer.asset             active PC PS1 renderer feature
  Resources/
    Materials/
      CityNoirEmission.mat
      HomeOccluderDither.mat       shared opaque Home foreground cutaway
      Ps1Composite.mat
      RuntimePrimitiveLit.mat      shared packaged URP/Lit runtime geometry
    Textures/
      CityGroundSoilAlbedo.png     generated compacted-soil ground; 512 runtime, Repeat/mips
      CityRoadAsphaltAlbedo.png    dark generated carriageway albedo; 512 runtime, Repeat/mips
      CitySidewalkAlbedo.png       retained light road texture, now used by sidewalks
      CityRoadMarkingAlbedo.png    generated worn white traffic-paint material tile
      CityFacadeOldTownBrickAlbedo.png      dark brick bond, soot, drip runs, one bricked-up opening
      CityFacadeOldTownStoneAlbedo.png      render blown off the brick shell, ghost of a removed sign
      CityFacadeResidentialCoolAlbedo.png   cold painted panel, seams, streaks under every window
      CityFacadeResidentialWarmAlbedo.png   same block repainted, one repaired panel, rust at fixings
      CityFacadeIndustrialSteelAlbedo.png   corrugated sheet, soot on horizontals, rust only at joints
      CityFacadeIndustrialRustAlbedo.png    utilitarian brick, boarded openings, painted-over marking
      CityFacadeNightlifeMagentaAlbedo.png  old shell under a commercial layer, dead mounts, bills
      CityFacadeNightlifeCyanAlbedo.png     the service side of that shell, dirtier, no shopfront
      CityRoofAlbedo.png                    felt strips, ponding and gravel for roof caps
                                            all nine 1024 source, 4x4 bay/floor cells; 512 runtime, Repeat/mips
    Rendering/
      Ps1PresentationProfile.asset  default 640x360, lower legacy presets
    Shaders/
      CityAtmosphereParticle.shader
      CityRiverWater.shader      quantized animated river flow with night/rain response
      HomeOccluderDither.shader   Forward+ grouped cutaway with shadow/depth/normals
      HomeWindowGlass.shader      shared transparent Home window/door glass
      Ps1Composite.shader         average, RGB555, intoxication distortion, point upscale
    Audio/
      Mixers/
        BarPromenadeAudio.mixer  shared groups, DSP returns and five scene snapshots
      CityMusic/
        city_theme.*  looping City theme
        README.txt
      BarMusic/
        bar_theme.*   looping BarInterior theme
        README.txt
      SupermarketMusic/
        supermarket_theme.*  optional looping SupermarketInterior theme
        README.txt
      StairwellMusic/
        stairwell_theme.*  optional looping StairwellInterior theme
        README.txt
      HomeMusic/
        home_theme.*  optional looping Home theme with Balcony pause/resume
        README.txt
      SmokingMusic/
        smoking_theme.*  optional Home balcony-vignette loop supplied by user
        README.txt
    Player/
      Player3D.prefab                   one production modular hero prefab
      Player3DPortrait.png              transparent inventory portrait from 3D model
    Pedestrians/
      CityPedestrian3D.prefab           pooled Lampshade Walker presentation
      ChairCarrierPedestrian3D.prefab   pooled Chair Carrier presentation
      KettleHatPedestrian3D.prefab      pooled stout Kettle Hat Walker presentation
      LongArmPedestrian3D.prefab        pooled narrow Long-Arm Walker presentation
      HelmetLampPedestrian3D.prefab     pooled hopping miner with one worn Spot
    Vehicles/
      CityBus3D.prefab                  passive real-scale pooled midibus presentation
      CityBusDriver3D.prefab            passive 31-bone seated production driver
    Home/
      Textures/                         twelve apartment RGB albedos; 1024 source, 512 runtime, Repeat/mips
        HomeWallpaperAlbedo.png         faded stripes, sprig stamps, picture ghosts, damp
        HomeCeilingPlasterAlbedo.png    whitewash, hairline cracks, water-stain rings
        HomePlankFloorAlbedo.png        painted boards, staggered joints, knots, scuff track
        HomeDarkWoodAlbedo.png          DSP/veneer grain, chipped edges, one repair patch
        HomeWornLaminateAlbedo.png      kitchen speckle, ring stains, scratches, burn
        HomeUpholsteryAlbedo.png        coarse weave, sagged shading, shiny worn patches
        HomeBedLinenAlbedo.png          dingy ticking, wrinkle folds, stain blotches
        HomeBathroomTileAlbedo.png      0.15 m tile grid, dark grout, cracked tiles
        HomeEnamelAlbedo.png            old white enamel, chip band, rust weeps
        HomePaintedMetalAlbedo.png      brush streaks, bolt lattice, rust bleeding down
        HomeConcreteAlbedo.png          stucco, offset formwork seams, damp streaks
        HomeRugAlbedo.png               diamond lattice, medallions, worn walking track
    Stairwell/
      Textures/                         eight active RGB albedos
        StairwellWallPaintAlbedoV2.png  active higher-contrast plaster/bands
        StairwellConcreteAlbedo.png     floor, ceiling and columns
        StairwellStairConcreteAlbedo.png  steps and landings
        StairwellCorrodedMetalAlbedoV2.png active rails/pipes/metal dressing
        StairwellDoorPaintAlbedoV2.png  active street/apartment door leaves
        StairwellDamageAlbedo.png       damp, chips and puddle
        StairwellDirtyWoodAlbedo.png    wardrobe and debris planks
        StairwellDebrisAlbedo.png       paper, bottle, mattress and sacks
      Cat/
        StairwellCatAtlas.png           512x256, 8x4 seated/look/grooming atlas
        StairwellCatFeedingAtlas.png    512x128, top-first 8x2 feeding atlas
    City/
      YardWheelchairProvider.asset  serialized link to the staged yard rider prefab
    Localization/
      ru.json
      en.json
  Player3D/
    Models/
      PlayerCharacter3D.fbx             production Generic model
      PlayerCharacter3D.json            deterministic parts/bones/actions manifest
    Animations/
      PlayerCharacter3DAnimations.fbx   26 in-place Actions; full-body 50-frame Rise L/R + bus trio
    Materials/
      Player3DLit.mat                   shared URP/Lit hero material
  Pedestrians/
    Models/
      CityPedestrian3D.fbx              compatible Generic street-walker model
      CityPedestrian3D.json             deterministic geometry/rig manifest
      ChairCarrierPedestrian3D.fbx      compatible Generic chair-bearer model
      ChairCarrierPedestrian3D.json     deterministic geometry/rig manifest
      KettleHatPedestrian3D.fbx         compatible Generic stout kettle-hat model
      KettleHatPedestrian3D.json        deterministic geometry/rig manifest
      LongArmPedestrian3D.fbx           compatible Generic narrow long-arm model
      LongArmPedestrian3D.json          deterministic geometry/rig manifest
      HelmetLampPedestrian3D.fbx        compatible Generic hopping miner model
      HelmetLampPedestrian3D.json       deterministic geometry/rig manifest
    Animations/
      CityPedestrianLocomotion.fbx      fourteen production loops + two staged Pipeback loops
      CityPedestrianLocomotion.json     gait/contact/clearance/apex + staged wheel-contact manifest
    Staged/
      Models/
        PipebackRoller3D.fbx            passive 31-bone wheelchair NPC model
        PipebackRoller3D.json           staged geometry/rig/passive-anchor manifest
      Prefabs/
        PipebackRoller3D.prefab         passive asset outside Resources and the runtime pool
  Vehicles/
    Models/
      CityBus3D.fbx                     real-scale exterior + modeled passenger cabin
      CityBus3D.json                    deterministic dimensions/bindings manifest
    Materials/                          14 shared URP bus surface materials
    Drivers/
      Models/
        CityBusDriver3D.fbx             exact 31-bone low-poly production driver
        CityBusDriver3D.json            deterministic parts/rig/bindings manifest
  City/
    Models/
      CityChessSet3D.fbx                six turned chessmen and a draught, board-scaled
      CityChessSet3D.json               deterministic heights/footprints manifest
  Scripts/
    Runtime/
      Core/          seven-scene bootstrap, city root, session, transitions
        CityGameRoot.cs           city composition + deferred debug-map arrival
        GameSessionState.cs       persistent clock/needs + one-shot debug-map handoff
        GameTimeState.cs          frozen 05:59 -> running 06:00, elapsed minute delta
        GameTimeRuntime.cs        persistent scaled-delta driver
        GameTimeDayNightRules.cs  night/dawn/day/dusk visual sample
        GameWeatherRules.cs       seeded 90-minute clear/light-rain/heavy-rain slots
        RuntimePrimitiveFactory.cs shared material primitives, oriented batches + opt-in XZ planar UVs
      Diagnostics/   bounded NDJSON session log, rotation and F8 snapshot
      Audio/         shared mixer routing, filtered themes and generated retro audio
        GameAudioMixer.cs                  canonical groups, snapshots and transitions
        CityRainSound.cs                   deterministic rain noise loop + intensity player
        CityThunderSound.cs                deterministic thunder one-shot + distance player
        SceneMusicPlayer.cs                unscaled entry/exit fade and pause envelope
        SupermarketMusicPlayer.cs          optional SupermarketInterior theme
        HomeMusicPlayer.cs                 Home theme + Balcony shot pause/resume
        HomeSmokingMusicPlayer.cs          optional interaction-local loop + gain envelope
        HomeAlarmClockSynthesis.cs         generated 22050 Hz mechanical ring
        InteriorSoundscapeSynthesis.cs    quantized PCM, fridge hum + lamp crackle
        InteriorSoundscapeAnchorPlanner.cs layout-derived spatial emitter anchors
      Rendering/     PC RenderGraph PS1 composite and settings
        IntoxicationRenderState.cs  world-effect parameters shared with the pass
      Games/         pure rules and engines for the two park boards, no Unity
        BoardGameContracts.cs  side/status/placement/action/turn contract both games answer
        ChessRules.cs          legal chess: make/unmake, castling, en passant, promotion, attack map
        ChessEngine.cs         negamax + piece-square tables + quiescence + slack pick
        ChessMatch.cs          the one file-mirror between chess coordinates and the drawn lattice
        DraughtsRules.cs       Russian draughts: compulsory chains, flying kings, Turkish strike
        DraughtsEngine.cs      negamax over snapshots + forced-capture extension + slack pick
        DraughtsMatch.cs       lattice-native adapter, capture-compulsory flag
      Map/           ordered road-route model and heap pathfinding
      World/         city plus validated bar/home/supermarket plans and builders
        CityBlueprint.cs         immutable areas, sparse cells, topology + fluent builder
        CityBlueprintCatalog.cs  default 13x12 river city with eastern Lake/Cemetery, five Yards + legacy blueprint
        CityRiverPlan.cs         10 m channel, dual promenades, three typed bridges + four lower landings
        CityRiverResources.cs    shared animated water material and night/rain factors
        CityElevationPlan.cs     node/cell datums, classified grades + authoritative height sampler
        CityElevationPlanner.cs  river-valley node profile, local lake basin + flat custom fallback
        CityElevationValidator.cs coverage, water, grade + four-district stair invariants
        CityElevationRebaser.cs  canonical lots, park, POIs, surfaces + access anchors
        CityTerrainSurfacePlan.cs continuous Buildable/Park/Open/Beach top and normal sampler
        CityTerrainSurfaceWorldBuilder.cs triangulated terrain meshes + matching mesh colliders
        CityElevationStairPlacement.cs  sidewalk flight/landing integration
        CityExteriorStair{Plan,Planner,Validator}.cs guarded exterior flight contracts
        CityExteriorStairWorldBuilder.cs visible steps + one hidden ramp collider per flight
        CityRoadGroundBoundaryPlan.cs endpoint-sampled safe-connector/protected-drop classification
        CityTerrainSafetyWorldBuilder.cs segmented physical guards along dangerous sampled drops
        CityVerticalTraversalPlan.cs deterministic seam/frontage audit + spawn-road reachability
        CitySurfacePlan.cs       typed ground/water cells (incl. Yard OpenGround), datum and open-area access
        CityStreetIntersectionSelector.cs  shared stable zebra/signal node selection
        CityBusIntersectionSelector.cs safe Road v2.1 corner/three-/four-way apron selection
        CityStreetSurfacePlan.cs immutable oriented carriageway/sidewalk/marking geometry
        CityStreetSurfacePlanner.cs  graded strips, level pads, stair cuts, dashes + zebras
        CityWorldBuilder.cs      continuous terrain, river/bridges, graded streets, stairs + guarded drops
        HomeYardSitePlan.cs      shared roadless-gap, rider-ring, neighbour-light + leaning-utility geometry
        CityOpenAreaDecorationPlan.cs  deterministic Lake + inter-building bar-side yard/light descriptors
        CityOpenAreaWorldBuilder.cs    chunked landmarks + fixed always-on neighbour-wall yard Spot
        CityCemeteryPlan.cs      oriented cemetery part/lamp descriptors, six grave variants + bounded budget
        CityCemeteryPlanner.cs   gate-framed alleys, hash-varied graves/оградки, trees, lamps + validation
        CityCemeteryWorldBuilder.cs  chunked oriented batches with cemetery sheets + night-scaled alley lamps
        CityCemeterySurfaceAppearance.cs  four cemetery albedos (granite/stone/gravel/soil) via MPBs
        CityCemeteryGroundWorldBuilder.cs  the cemetery slab, rebuilt around every open grave
        CityCemeteryGroundExcavation.cs   the register of open holes; cut and fill both rebuild the slab
        CityCemeteryPitWorldBuilder.cs    collar, floor, spoil heap + the cap that keeps the hero out
        CityHandLampWorldBuilder.cs      the shared kerosene hand lamp: pier head and graveside, one fixture
        CityCemeteryCoffinWorldBuilder.cs  six-sided turned-board coffin, overhanging lid, cross
        CityCemeterySealedGraveWorldBuilder.cs  turned mound courses + one planner monument, slab omitted
        CityLakePlan.cs          oriented lake part/lamp descriptors, four hull variants, basin + budget
        CityLakePlanner.cs       inset cut-cornered waterline, revetment ring, pier, hulls, hut + validation
        CityLakeWorldBuilder.cs  chunked oriented batches with lake sheets + night-scaled shore lamps
        CityLakeSurfaceAppearance.cs  three lake albedos (plank/bank/hull) via MPBs
        CityLakeBankMeshFactory.cs  walkable collidered bank ring + colliderless silt bed cap
        CityLakeResources.cs     the still-water material: zero flow, isotropic ripple, lamp glint
        CityWaterResources.cs    the one night/rain drive shared by every water material
        CityParkSurfaceAppearance.cs  eight park albedos (lawn/path/plaza/bark/foliage/timber/stone/painted metal) via MPBs
        CityDistrict.cs          area IDs, district/path/land-use enums and park data
        CityTravelDistance.cs    weighted road/park-path distance between bars
        CityDistrictPointOfInterestPlan.cs  kinds, public bounds and street accesses
        CityDistrictPointOfInterestPlanner.cs  primary/public reservations + 18 m guard
        CityDistrictPointOfInterestWorldBuilder.cs  four physical open-place recipes + drying yard and island mast floodlights, carpet rack and babushka stances
        CityPointOfInterestSurfaceAppearance.cs  five scripted POI albedos (paving/metal/cloth/timber/paper) via MPBs
        CityDecorationDescriptor.cs  24 visual families and anchor contracts
        CityDecorationPlan.cs        immutable ordered seeded decoration data
        CityDecorationPlanner.cs     primary landmarks, lot visuals, tiers, clear clusters + spaced booth/dumpster coverage incl. bar-side yard pair
        CityDecorationValidator.cs   landmark/core quotas, IDs and clearances
        CityDecorationWorldBuilder.cs  six-style visuals, chunked collision proxies + utility dock read-back
        CityStreetUtilityDock.cs     booth-door/dumpster-lid docks the interactions stand on
        CityStreetUtilityWorldBuilder.cs  one placeholder trigger per utility dock
        CityBoardGamePlan.cs     playable tables, seated eye pose + board-plane square picking
        CityBoardGamePieces.cs   live men on one table: pooled views, carries, sweeps and crowns
        CityBoardGameMarkers.cs  pooled emissive plates for hover/selection/destinations/check
        CityChessSetMen.cs       the four static batches, remembered per table so one can be hidden
        CityStaticCollisionBuilder.cs  tier catalog + decoration/park/pole box proxies
        CityExteriorAppearance.cs    shared City/Home ground + three street MPB recipes + window family resolver
        CityWindowAppearance.cs      windowed-pane sheet, five shared lit materials on the night factor
        CityNightGlowRegistry.cs     registered electric glows (neon/signs/lamps) that die by day
        CityNightSiteLightRegistry.cs  authored site realtime lights scaled/disabled by the night factor
        CityFacadeGrid.cs            single source of the bay/floor pitch both walls and windows read
        CityFacadeAppearance.cs      district wall albedos tiled by that grid, not by metres
        CityBarFacadeWorldBuilder.cs shared passive bar-front identity + 3D blade sign
        CitySupermarketFacadeWorldBuilder.cs  shared branded supermarket storefront
        SupermarketEntranceGeometry.cs  frontage, apron and fence-opening dimensions
        RoadFencePlan.cs         MapBoundary/DeadEnd rails + clearance-opening metadata
        RoadFencePlanner.cs      unsupported footprint edges + true Street terminals
        CityNightFixturePlanner.cs  lamps/signals clear public ground and approaches
        CityDayNightController.cs   session lighting + exterior night factor
        CityWeatherController.cs    per-frame weather sample -> rain, flash, thunder
        CityRainField.cs            seeded player-following stretched rain streaks
        CityLightningFlashLight.cs  transient shadowless directional storm flash
        RoadWalkableArea.cs      ground/road/river-promenade union + sampled boundary-safe connectors
        HomeInteriorLayout*.cs   main/bath paths, nine footprints and corner blocker
        HomeOcclusionRegistry.cs explicit logical renderer groups and visibility floors
        PlayerHomeBalconyGeometry.cs  shared City/Home facade transform and dimensions
        HomeBalconyLayout*.cs    connected room/threshold/deck walkable plan
        HomeExteriorContextPlan.cs  bounded street/decoration/pedestrian + Home-stop context
        HomeBalconyWorldBuilder.cs   window, open door, deck, safe rails + permanent ashtray
        HomeExteriorViewBuilder.cs   collider-free lots/windows/lights + static Home stop
        HomeBedInteractionPlan.cs  open-side trigger + separate entry/action/exit poses
        HomeBalconySmokingPlan.cs  entry/exit poses, trigger, camera + 24/24/16 timing
        HomeRefrigeratorPlan.cs  body/approach/camera/audio anchors + eight slots
        HomeRefrigeratorWorldBuilder.cs  worn hollow cabinet, shelves, bins and contents
        HomeRefrigeratorView.cs  animated door/handle/emissive interior presentation
        HomeRefrigeratorItemCatalog.cs  localized metadata and preview transforms
        HomeRefrigeratorItemView.cs  stable renderers, selection trigger and original root
        HomeAlarmClockPlan.cs       validated bed-relative nightstand/clock placement
        HomeAlarmClockBuilder.cs    low-poly nightstand and alarm-clock composition
        HomeBathroomBuilder.cs   oriented toilet, shower/sink and pipe damage
        HomeInteriorDressingBuilder.cs  collider-free poverty/neglect details
        SupermarketInteriorLayout*.cs  room/aisles/fixtures, 3 shelves + 5 slots
        SupermarketInteriorWorldBuilder.cs  worn shop, finite products, checkout shell
        SupermarketSecurityCameraWorldBuilder.cs  four corner CCTV heads servoed at the hero
        Supermarket{Shelf,Product}View.cs  registered physical stock and source IDs
        StairwellLayout*.cs      three elevations, connected flights and blocker
        StairwellWorldBuilder.cs stairs, landings, rails, doors and physical ramps
        StairwellDressingBuilder.cs pipes, vents, stains, trash and upper debris
        StairwellSurfaceAppearance.cs  cached recipes + projection-aware UV MPBs
        SurfaceAppearanceCore.cs  shared projection/tiling/hash/tint math for surface pipelines
        HomeSurfaceAppearance.cs  twelve cached home recipes + HomeSurfacePrimitives wrappers
      Stairwell/Cat/ deterministic perch, idle/look and feeding presentation
        StairwellCatFeedingPlan.cs          safe middle-shot entry/action/exit poses
        StairwellCatFeedingTimeline.cs      16-frame, 6 fps one-shot cat track
        StairwellCatFeedingSpriteLibrary.cs top-first 8x2 point-sprite slicing
      City/NPC/      local player-relative graph walkers with two reusable slots
        CityBenchRestPlan.cs           shared seat claims + reachable-bench rest points
        CityBenchNpcRestController.cs  sends nearby walkers to free benches for 15-30 s
        CityPedestrianPlan.cs          immutable nodes, links and spawn anchors
        CityPedestrianPlanner.cs       height-sampled sidewalks, stairs + zebra connector graph
        CityPedestrianActor.cs         forward graph walk, seeded zebra choice + Route 01 and bench-rest states
        CityPedestrianDirector.cs      fog-band lifecycle, safe pooling + yielding
        CityPedestrianPresentation.cs  archetype Idle/Walk/Sit blend, grounding + seat alignment
        CityPedestrianAssetRegistry.cs prefab anchors, clips and MPB palettes
        CityWheelchairNpcAssetRegistry.cs passive future mechanism-pivot bindings; metadata only
      Yard/          the authored rider on the bar-side yard circuit, outside the ambient pool
        YardWheelchairMotion.cs      pure drift pose; computes the reserved wheel differential
        YardWheelchairPlan.cs        circuit read back from the authored ring and dead tree
        YardWheelchairPresentation.cs two-clip skeletal graph; mechanism pivots reserved/future
        YardWheelchairActor.cs       owns distance along the ring and applies the pose
        YardWheelchairFactory.cs     one instance, passivity re-checked at instantiation
        YardWheelchairProvider.cs    the only serialized reference to the staged prefab
        DryingYardBabushkaPlan.cs    three authored stances off the drying yard descriptor
        DryingYardBabushkaPresentation.cs  one-clip manual PlayableGraph + role prop enabling
        DryingYardBabushkaFactory.cs three instances, passivity re-checked at instantiation
        DryingYardBabushkaProvider.cs  the only serialized reference to the staged prefab
      Weighbridge/   the authored pair on the Industrial cold weighbridge + the answering needle
        WeighbridgeAttendantPlan.cs  two authored stances + the deck rect off the weighbridge descriptor
        WeighbridgeAttendantPresentation.cs  one-clip manual PlayableGraph; corridor travel slaved to clip time
        WeighbridgeAttendantFactory.cs two instances, passivity re-checked at instantiation
        WeighbridgeAttendantProvider.cs  the only serialized reference to the staged prefab
        CityWeighbridgeNeedleController.cs  City-root needle deflection under NPC pause or hero weight
      Lake/          the boat station's one permanent inhabitant
        LakeFishermanProvider.cs  the only serialized reference to the staged prefab
        LakeFishermanPlan.cs     stance, facing and waterline read back from the lake plan itself
        LakeFishermanQuips.cs    15-line seeded repertoire, never twice running, never second person
        LakeFishermanInteraction.cs  talk stub on a trigger docked behind him, not in front
        LakeFishermanPresentation.cs  single-clip manual PlayableGraph; publishes the loop's breath phase
        LakeFishermanRigAnchors.cs  bind-pose anchors for the pipe bowl and the rod point
        LakeFishermanPipeEffect.cs  ember, its point light and the plume, all on the breath phase
        LakeFishermanLine.cs     line struck from the live rod tip to the measured waterline
        LakeFishermanFactory.cs  one staged instance, passivity validated, magnet + talk trigger + pipe + line
      Cemetery/      the scripted graveside visitor summoned by the hero's presence
        CemeteryMournerPlan.cs   pure grave candidates, foot-side stand, gate route, unseen spawn + trigger band
        CemeteryMournerTimeline.cs  approach/lay/cry(30 s)/wipe/depart clock with the one-shot lay cue
        CemeteryMournerPresentation.cs  two-clip manual PlayableGraph + hand-bouquet hiding
        CemeteryMournerFactory.cs  one transient instance per visit, passivity re-checked
        CemeteryMournerProvider.cs  the only serialized reference to the staged prefab
        CityCemeteryMournerController.cs  City-root proximity trigger, spawn/route/cooldown + the laid bouquet
        CemeteryWatchmanPlan.cs  doorstep stance read back from the plan's own lodge parts
        CemeteryWatchmanQuips.cs  seeded 15-line snide repertoire, never the same twice running
        CemeteryWatchmanInteraction.cs  cashier-contract talk stub serving the next quip
        CemeteryWatchmanPresentation.cs  one-clip manual PlayableGraph (the watch loop)
        CemeteryWatchmanFactory.cs  one permanent instance + its own talk trigger
        CemeteryWatchmanProvider.cs  the only serialized reference to the staged prefab
        CemeteryGraveWorkStage.cs  the monotone ladder the whole worksite is rebuilt from
        CemeteryGravediggingPlan.cs  hole, spoil, lamp seat, heading + the stone this plot will wear
        CemeteryGravediggingController.cs  the four acts, their world, restore and the wage
        CemeteryGraveDigSiteInteraction.cs  one staged worksite stub: dig, lower, fill, set
        ICemeteryGraveWorkSession.cs  the seam that lets an act be earned instead of pressed
        CemeteryGraveWorkController.cs  the modal session: camera, hero lease, input, commit
        CemeteryGraveWorkStance.cs  where he stands, which way he throws, what the shot sees
        CemeteryGraveLatticeModel.cs  6 x 3 courses, the no-pillar rule and the ground per course
        CemeteryGraveSoil.cs  turf/loam/clay/stone/root/spoil and how each answers the spade
        CemeteryStrokeModel.cs  the swing bar, and the one rule that judges a strike
        CemeteryCoffinLowerModel.cs  two ropes, tilt and the slip that drops the box
        CemeteryStoneSettleModel.cs  heaving it upright, then three blows to set it
        CemeteryEpitaph.cs  the eight words a plaque holds, counted and cut
        CemeteryPlaqueReadInteraction.cs  the finished grave's board, read again
        (World) CityCemeteryPlaqueWorldBuilder.cs  the board, fitted to the stone's measured face
        (World) CemeteryPlaqueFont.cs  a runtime TMP font asset off a Cyrillic .ttf
        (World) CemeteryPlaqueSurface.cs  the board's three lines, reset when the line is cut
        (UI) CemeteryPlaqueView.cs  the three lines the board actually says
        CemeteryShovelAnimator.cs  drive, lever, lift, dump — the only animated thing here
        CemeteryGraveTrestle.cs  two bearers, four slings, and the coffin pose they imply
        (World) CityCemeteryCoffinRestWorldBuilder.cs  the two blocks the box waits on
        (World) CityGravediggerShovelWorldBuilder.cs  the spade, and where it stands between acts
      Park/          the two old men's boards, once somebody sits down at one
        CityBoardGameController.cs  seat hookup, seated camera ownership, pointer/cursor input, opponent think clock + every board state as a spoken cue
      Vehicles/      one-slot real-scale Route 01 bus, passenger ride and presentation
        CityBusPlan.cs             immutable ordered Route 01 loop, target-owned stops + occurrences
        CityBusPlanner.cs          grade-safe Street graph, 3D samples + full-body clearance proof
        CityBusTargetRoutePlanner.cs grounded POI/Home candidates + deterministic winding loop solver
        CityBusWideTurnPlanner.cs  level-apron two-edge safe-right macro between graded links
        CityBusActor.cs            3D fixed-loop motion/pitch, 10 s dwell + service ownership
        CityBusDirector.cs         fog spawn, passenger-safe recycle and forced-cleanup lifecycle
        CityBusRidePlan.cs         local-surface door transfer + level camera geometry
        CityBusRideController.cs   prompts, board/ride/alight, ride look input + exact cleanup
        CityBusStopWaitPlan.cs     per-stop pavement wait slots + stop-seeded graph distances
        CityBusStopWaitPlanner.cs  locally grounded slot geometry + single-source Dijkstra
        CityBusNpcPassengerController.cs ambient waiters, seated boarding, random alighting
        CityBusStopWorldBuilder.cs physical City poles + collider-free Home-local pole
        CityBusDriverDoorTimeline.cs deterministic approach/dwell hand, button + look samples
        CityBusDriverPresentation.cs seated IK, door/player focus, rubber-neck stretch + blink
        CityBusDriverAssetRegistry.cs exact 31-bone passive rig bindings
        CityBusDriverResources.cs  passive driver Resources prefab loading
        CityBusPresentation.cs     sprung body, controls, driver handoff, emission + night Spots
        CityBusAssetRegistry.cs    dimensions, bounds, articulation + interior bindings
        CityBusResources.cs        passive Resources prefab loading
        CityBusFactory.cs          physical slot/layer composition + validation
      Player/        motor, presentation contracts, chase/fixed cameras and contact shadow
        PlayerMotor.cs             grounded guided approach + no-progress cancellation
        PlayerPresentation.cs      3D motion/status/clip/visibility contracts
        PlayerFactory.cs           shared prefab spawn in all five gameplay roots
        PlayerAttention.cs         Silent Hill head: notice cone rules, target picker + magnets
        PlayerCameraFollow.cs      bounded yaw/pitch chase, fixed pose + shared orbit sampling
        PlayerContactShadow.cs     slope-aligned planted/fall-aware analytic ground patch
        PlayerNeedsProgressionState.cs  fractional clock-driven hunger/fatigue
        PlayerNeedsRules.cs        shared 0-100 need bounds + hunger/stress relief
        IntoxicationStageRules.cs   five ranges and interpolated profiles
        BalanceChallengeModel.cs    seeded schedule and fixed-step arrow model
        PlayerFallAnimationTimeline.cs  14/36/50 authored phase mapping, 100 total
      Player3D/
        Player3DAssetRegistry.cs        serialized meshes, parts, bones, sockets, Actions
        Player3DResources.cs            safe Resources prefab instantiation
        Player3DCharacterPresentation.cs clips + physics handoff + full-body Rise sampling
        Player3DRagdollController.cs     bounded 13-body failed-balance physics + pose recovery
        Player3DFirstPersonSubset.cs     prefab-derived camera-local arm filtering
        Player3DHeadVisibility.cs        the whole head off by bone rule, for a camera inside it
      Inventory/     pure item catalog, ordered session stacks and menu state
        InventoryTypes.cs           stable IDs, definitions and stack values
        InventoryState.cs           atomic bounded stack mutations + starters
        InventoryConsumableCatalog.cs food floors, relief and bottled servings
        InventoryMenuModel.cs       wrapping selection and examine state
        InventoryItemModelFactory.cs shared low-poly world/preview item models
        HomeRefrigeratorInventoryAdapter.cs  slot sources -> inventory IDs
        SupermarketProductCatalog.cs five offers with localized metadata/prices
        SupermarketPurchaseRules.cs  pure finite-source/cash/stack validation
      Interaction/   contracts, shops and bar/home/stairwell/supermarket doors
        InventoryTargetInteraction.cs   reusable item requirement/menu state/handler contract
        PlayerAnimatedInteraction*.cs  positioning, static/moving pelvis targets + independent exit
        HomeBedInteraction.cs          first-E sleep, persistent loop, completed-wake fatigue reset
        HomeBalconySmoking{Interaction,Timeline}.cs  safe exit + camera push/drift + music envelopes
        HomeRefrigeratorInteraction*.cs  outer modal first-person open/inspect/close timeline
        HomeRefrigeratorItemInspection*.cs  nested hover/fly/rotate/return controller + timeline
        HomeRefrigeratorFirstPersonHand.cs  prefab-derived right arm and handle reach
        StairwellCatInteraction.cs     Talk/Interact adapter + paired feeding orchestration
        HomeBathroomSceneInteraction.cs  shared bathroom-scene skeleton: modal, walk-in, camera
        HomeToiletInteraction.cs       privacy-cut toilet scene + pure timeline
        HomeShowerInteraction.cs       curtain/water/steam shower scene + timeline + effect
        HomeTeethBrushingInteraction.cs  mirror close-up, CCD brushing arm, foam, day-gated relief
        Supermarket{Entrance,Exit}.cs  separate-scene round trip and return context
        SupermarketShelf{Station,ShopController,ShopView}.cs  physical shelf browser
      Scenes/        startup/bar/home/stairwell/supermarket roots and presentation
        MainMenuRoot.cs                 black build-index-0 new-run boundary
        HomeOpening*.cs                5 s gate, 3 s post-Wake alarm and 2x wake
        HomeDebugCityMapShortcut.cs     Home F9 -> City/home return + one-shot debug map request
        HomeAlarmClock.cs              session-following 28-segment time, ring and rattle
        HomeDayNightController.cs      window and balcony time-of-day lighting
        HomeSoundscape*.cs               louder fridge hum, lamp crackle + domestic cues
        StairwellSoundscape*.cs          uneasy spatial beds and industrial cues
        HomeFixedCameraController.cs  three fixed shots + activation/hold hysteresis
        HomeBalconyExteriorAtmosphere.cs  Balcony-only City fog/lights + pedestrian gate
        HomeOcclusionResolver.cs      five camera-to-player sample rays
        HomePlayerOcclusionController.cs  grouped dither fade/hold/restore
        HomeInteriorAtmosphere.cs     two practicals + bathroom/window Spots, grade and dust
        HomeBathroomLight*.cs         synchronized tube/halo/point/spill flicker
        StairwellFixedCameraController.cs  three height-selected fixed shots
        StairwellInteriorAtmosphere.cs flickering practicals, grade and dust
        SupermarketInteriorRoot.cs    layout/world/player/shop/UI composition
        SupermarketInteriorAtmosphere.cs  six shadowless practicals + flickering row
        BarInteriorRoot.cs            bar layout/world/patrons/drink-shop composition
        BarPatronWorldBuilder.cs      pooled 3D guests on NPC anchors, seated via seat contract
      Drinks/        stable IDs, retail catalog, atomic purchases and shop UI
      Supermarket/Cashier/  the Watcher Cashier: provider-bound passive prefab
        SupermarketCashierProvider.cs      one addressable ref to the off-Resources prefab
        SupermarketCashierAssetRegistry.cs bones, five neck pivots, renderer bindings
        SupermarketCashierFactory.cs       spawn on the plan anchor + passivity guard + magnet
        SupermarketCashierPresentation.cs  procedural hunch, periscope chain, pupils, blink
        SupermarketCashierActor.cs         samples the hero, drives surveillance + pose
        SupermarketCashierSurveillanceState.cs pure periscope/startle/blink-suppression logic
        SupermarketCashierBlinkState.cs    pure 6.5 s rare-blink cycle
        SupermarketCashierInteraction.cs   E talk stub on its own trigger
      UI/            retro UI, pause/inventory, segmented HUD, district/bus map and F9 debug
        BalanceCheckView.cs         crisp overhead arc, arrow and risk meter
        CityMapBusOverlay.cs        simplified blue loop + ordered localized stop markers
        CityMapController.cs        bus overlay, canonical lots, debug teleport + bar route
        CityMapView.cs              bus/stop legend plus shop/POI/bar map presentation
        PauseMenuModel.cs           pure main/confirmation navigation and actions
        PauseMenuController.cs      shared-lock time/audio/input pause ownership + IMGUI
        InventoryController.cs      modal inventory, selection and atomic Eat/Drink input
        InventoryView.cs            640x360 status + HH:MM/grid/description/command UI
        InventoryIconLibrary.cs     point-filtered icons + dedicated 3D hero portrait
        InventoryItemPreviewRenderer.cs hidden live 3D RenderTexture stage
        InventoryTargetInteractionController.cs shared modal target menu + atomic consumption
        InteractionPromptView.cs    localized clickable contextual actions
        HomeRefrigeratorItemInspectionView.cs  hover label and PS1 item panel
    Editor/          scene/build helpers and reproducible noir/PS1/audio asset setup
      AudioMixerAssetSetup.cs  idempotent shared mixer topology and snapshot authoring
      Player3D/       deterministic model/animation/portrait import + prefab setup
      City/NPC/       production/staged pedestrian Generic import, validation + prefab setup
      City/Traffic/   bus/driver FBX import, shared materials + Resources prefab setup
  Tests/
    Infrastructure/  shared run callback: mute listener output, then restore it
    EditMode/        layout plans, mixer DSP contract, sound synthesis and gameplay rules
      RuntimePrimitiveFactoryTests.cs four exterior assets/import/seam/MPB/UV contract incl. box-projected world UVs
      CityParkSurfaceAppearanceTests.cs  eight park sheets: recipes/import/source contract, UV mode, textured lawn/park build + landmark-only decoration texturing
      AutomaticTestAudioMuteTests.cs       run-level mute registration contract
      PauseMenuModelTests.cs               wrapping navigation and destructive confirmation
      Inventory{State,MenuModel}Tests.cs   stacks, starters and grid navigation
      PlayerNeedsRulesTests.cs        relief floors, clamping and drink fractions
      PlayerNeedsProgressionStateTests.cs  rates, chunking, cap and fractional reset
      GameSessionStateTests.cs        clock-driven needs and atomic session transactions
      InventoryConsumableCatalogTests.cs current food/alcohol value table
      InventoryTargetInteraction{Model,Controller}Tests.cs  safe defaults, commit and cleanup
      InventoryPresentationTests.cs       icons, dedicated 3D portrait and item models
      Player3D/Player3DAssetImportTests.cs  model/Actions/parts/sockets/prefab contract
      CityStreetSurfacePlannerTests.cs  corridor split, zebra selection + dash exclusion
      CityTerrainSurfaceWorldBuilderTests.cs sampled mesh/collider/UV terrain contract
      CityVerticalTraversalAuditTests.cs     continuous seams + spawn-road reachability
      CityPedestrianPlannerTests.cs     deterministic radius-safe sidewalk routes
      CityPedestrianRuntimeTests.cs     production lifecycle + staged Pipeback isolation/bindings
      CityBusPlannerTests.cs            winding target loop/stops + turn-envelope proof
      CityBusRuntimeTests.cs            encounter/loop/dwell plus passenger holds, recycle guards + reset
      CityBusRidePlayModeTests.cs       both-door prompt, ride/next-stop exit + state restoration
      CityBusAssetImportTests.cs        dimensions, interior, wheel/button controls + passive prefab
      CityBusDriverDoorTimelineTests.cs phase, contact + chunk-independent samples
      CityBusDriverAssetContractTests.cs 31-bone rig, eyes, shared material + passive prefab
      CityMapBusOverlayTests.cs         closed simplification + numbered stop projection
      HomeBalconyLayoutTests.cs         Home exterior layout/pedestrians + static stop pole
      SupermarketCityPlanningTests.cs     one eligible lot + open street approach
      CityOpenAreaDecorationPlannerTests.cs  Lake identity, clearance and determinism
      CityCemeteryPlannerTests.cs         cemetery variety/clearance/determinism, textured build, night lamps + sittable benches
      CityMapViewportTests.cs             independent overflow axes, focus and clamping
      SupermarketInteriorLayoutTests.cs   room, paths, fixtures and finite slots
      SupermarketPurchaseRulesTests.cs    five offers, atomicity and new-run reset
      CatFeedingAnimationAssetTests.cs    cat sprite-track import and timing contract
      StairwellSurfaceAppearanceTests.cs  8 imports, shared MPBs and renderer coverage
      HomeSurfaceAppearanceTests.cs  12 imports, linear tint rule, dither `_BaseMap` guard and walk coverage
      StairwellCat{Interaction,Runtime}Tests.cs  branches, staging and feeding timeline
      ProjectBuildSceneTests.cs             startup scene order/allow-list
      HomeOpeningTimelineTests.cs           persistent 05:59 flicker and Wake-only 06:00
      GameTimeStateTests.cs                 freeze/start/elapsed delta/day/midnight/reset
      GameTimeDayNightRulesTests.cs         phase boundaries and smooth transitions
      GameWeatherRulesTests.cs              slot determinism, targets and boundary ramps
      HomeAlarmClockPlanTests.cs            clock placement and circulation
      HomeRefrigerator{Plan,Timeline}Tests.cs  slots, approach and phase channels
      HomeBalconySmoking{Plan,Timeline}Tests.cs  dock, 3D clips, timing, drift + safe exit
      HomeRefrigeratorItem{Catalog,InspectionTimeline}Tests.cs  metadata and nested phases
      HomeOcclusion{Registry,Resolver}Tests.cs  group and ray contracts
      InteractionPromptViewTests.cs          prompt callback lifecycle
      InteriorSoundscapeSynthesisTests.cs   deterministic distinct loop contracts
      Audio/HomeAlarmClockSynthesisTests.cs generated ring contract
    PlayMode/        audio routing/lifecycle, presentation, traversal and scene flow
      AutomaticTestAudioMutePlayModeTests.cs  silent listener-output contract
      PauseMenuPlayModeTests.cs            Escape, modal exclusion and exact restoration
      InventoryPlayModeTests.cs            I/Escape, clock/needs freeze and exact restoration
      SupermarketPurchasePersistencePlayModeTests.cs  music bootstrap + buy/remove/re-enter contract
      StairwellInteriorPresentationPlayModeTests.cs  Talk/missing/feed GPU lifecycle
      HomeOpeningPlayModeTests.cs           launch, wake, Home F9 map skip and cleanup
      HomeBalconyPresentationPlayModeTests.cs  time/fog invariants + pedestrian gate
      HomeAlarmClockPlayModeTests.cs        spatial source/rattle/cleanup
      HomeRefrigerator*PlayModeTests.cs     storage, hover, nested inspection and restoration
      HomeBalconySmokingInteractionPlayModeTests.cs  rail ashtray, 3D clips, mouth plume, drift + restore
      HomeSmokingMusicPlayerPlayModeTests.cs optional clip and mixer-safe lifecycle
      HomeMusicPlayerPlayModeTests.cs      missing-track and Balcony-zone lifecycle
      SceneMusicPlayerPlayModeTests.cs     fade, pause/resume and scene-exit contracts
      HomePlayerOcclusionControllerPlayModeTests.cs  lifecycle + dither/Forward+ GPU checks
      InteriorSoundscapePlayModeTests.cs    spatial routing, crossfade and lifecycle
      Player3DOrdinaryPresentationPlayModeTests.cs  locomotion/status/falls/all-fours/shadow
      IntoxicationStatusPlayModeTests.cs hybrid handoff, fixed root, one-phase Rise cleanup
      PlayerAnimatedInteraction3DPlayModeTests.cs   clip sampling, pelvis alignment and cleanup
      Player3DGameplaySceneIntegrationPlayModeTests.cs  all five gameplay roots
      Player3DVisualCapturePlayModeTests.cs  bounded scene framing capture
      BarDrinkFirstPersonArmsPlayModeTests.cs  prefab subsets + visibility restoration
ArtSource/
  Vehicles/
    Blender/                    generated bus .blend and deterministic preview
    Drivers/Blender/            generated driver .blend and deterministic preview
  Pedestrians/
    Blender/                    five production + one staged model .blends/previews and shared locomotion contact sheet
  Player/
    PlayerDirectionalTurntable.png  retired 2D design source / visual lineage
    Blender/                    production .blend, transparent preview and authoring notes
    BedSleep/                    retired player-sprite source history
    BalconySmoking/              retired player-sprite source history
    CatFeeding/                  retired player-sprite source history
  Stairwell/
    Cat/Feeding/                 raw/keyed 4x4 cat source + top-first contract
  City/
    Facades/                     facade albedo contract, contact sheet and the cell-grid README
    Blender/                     generated park chess-set .blend and the six-silhouette review row
  Home/                          apartment albedo contract, manifest and contact sheet
tools/
  build-city-bus-3d-model.py         real-scale bus model/export validator
  build-city-bus-driver-3d-model.py  driver model/rig/export validator
  build-city-pedestrian-3d-model.py  compatible rig/model/export validator
  build-city-chess-set-3d-model.py   turned chessmen/draught meshes + height-ladder validator
  build-player-3d-model.py          model/Actions/portrait + full-body Rise validators
  build-player-puppet-atlas.py      retired 2D player source tooling
  extract-player-bed-sleep-frames.py      retired player-sprite source tooling
  build-player-bed-sleep-atlas.py         retired player-sprite source tooling
  extract-player-balcony-smoking-frames.py retired player-sprite source tooling
  build-player-balcony-smoking-atlas.py   retired player-sprite source tooling
  build-player-cat-feeding-atlas.py       retired player-sprite source tooling
  build-stairwell-cat-feeding-atlas.py  validate and pack the top-first 8x2 atlas
  build-city-facade-textures.py     deterministic district wall albedos + validator
  build-city-poi-textures.py        deterministic district POI surface albedos + validator
  build-cemetery-textures.py        deterministic cemetery surface albedos (granite/stone/gravel/soil) + validator
  build-city-park-textures.py       deterministic park surface albedos (ground, objects and landmark materials) + validator
  build-home-textures.py            deterministic apartment surface albedos + validator
Packages/
ProjectSettings/
```

Cross-system flow:

```text
build index 0 -> MainMenuRoot -> BeginNewGame
                              -> HomeArrival.OpeningSleep
                              -> Single-load HomeInterior
                              -> time frozen at 05:59
startup Wake or accepted Home F9 debug skip -> session time 06:00
                                             -> GameTimeRuntime scaled delta
                                  -> 1440 real seconds per game day
                                  -> PlayerNeedsProgressionState
                                     -> hunger 0..100 / 1440 game minutes
                                     -> fatigue 0..100 / 1080 game minutes
                                     -> integer inventory Status bars
                                  -> HomeAlarmClock HH:MM
                                  -> CityDayNightController
                                  -> HomeDayNightController
blueprint ID + seed -> CityBlueprintCatalog -> immutable CityBlueprint
                                          -> stable area IDs + categories/profiles
                                          -> sparse active-cell topology
                                          -> north-south river corridor
                                             -> two road bridges + timber park bridge
                                             -> two promenades + four lower landings
                                          -> split 16-cell centered park
                                          -> north-edge beach + water
                                          -> default eastern Lake/Cemetery areas
                                          -> CityLayoutGenerator -> validated CityLayout
                                           -> 13x12 envelope preserving 144 lots
                                           -> four UrbanBuilt areas + central park
                                           -> typed surfaces + open-area accesses
                                           -> distant bars via CityTravelDistance
                                           -> player home beside one bar street
                                           -> nearest eligible Residential supermarket
                                              -> branded facade + walkable apron
                                              -> stable City return point
                                           -> four first-class public lots
                                              -> only at >= 18 m lot width and depth
                                              -> waterworks court
                                              -> drying yard
                                              -> weighbridge
                                              -> grounded non-emissive last-route island
                                           -> shared third-floor balcony facade geometry
                                           -> fresh road-node spawn beside the home
                                            -> RoadWalkableArea
                                               -> streets + park + OpenLand
                                               -> promenades, bridges + lower landings
                                               -> complete BuildableGround regions
                                               -> radius-safe road/ground seams
                                               -> water/unmapped/outside excluded
                                               -> physical colliders own obstacles
                                              -> PlayerMotor
                                          -> CityRoutePathfinder
                                             -> district-aware CityMap
                                                -> clipped readable viewport
                                                -> independent X/Y pan when overflowing
                                           -> RoadFencePlanner
                                              -> water/unmapped/map-boundary rails
                                              -> true degree-one Street caps
                                              -> Street + ParkPath degree accounting
                                              -> openings retained as clearance metadata
                                          -> CityNightFixturePlanner
                                             -> public reservations stay clear
                                             -> chunked lamps + signals
                                          -> CityDistrictPointOfInterestWorldBuilder
                                             -> physical paving + free-standing recipes
                                             -> intentional surface/obstacle colliders
                                          -> CityDecorationPlanner
                                             -> one ordinary-lot visual each
                                             -> four primary urban landmarks
                                             -> two park landmarks
                                             -> frontage/roadside/park clusters
                                             -> CityDecorationWorldBuilder
                                                -> six shared visual styles
                                                -> shadowless 48 m chunks
                                                -> tiered simple collision proxies
                                           -> CityMap
                                              -> centered sparse blueprint bounds
                                              -> canonical area surfaces and labels
                                              -> river + three typed bridge styles
                                              -> public-place descriptors + marker legend
                                          -> Home exterior context
                                             -> nearby canonical public places
                                             -> local-space visual reconstruction
session time -> GameTimeDayNightRules -> CityDayNightController
                                     -> directional/ambient/reflection lighting
                                     -> CityNightAtmosphere night factor
                                        -> bounded lights + CityLightHalo
player + seed -> CityFogField (unchanged by time of day)
seed + session time -> GameWeatherRules -> CityWeatherController
                                        -> CityRainField streaks (bus-ride safe core)
                                        -> CityRainSoundPlayer volume/cutoff
                                        -> storm windows -> CityLightningFlashLight
                                                         -> CityThunderSoundPlayer delay
                                        -> CityBusDirector rain provider -> wiper sweep
layout -> CityStreetSurfacePlanner -> Road v2: `8 m` street / `6 m` carriageway
                                 -> two raised `1 m` sidewalks
                                 -> ordinary clear `6 x 6 m` junction apron
                                 -> Road v2.1 selected bus nodes
                                    -> perpendicular corners + three-/four-way junctions
                                    -> four `1 m` corner pads displaced outward
                                    -> complete clear `8 x 8 m` asphalt apron
                                    -> may share flat zebra paint + paired signals
                                 -> white center dashes + selected zebra crossings
                                 -> shared City/Home presentation geometry
                                 -> sidewalk/crosswalk walkable rectangles
layout + seed -> CityPedestrianPlanner -> sidewalk/turn/zebra graph
                                      -> radius-safe navigation corridors
                                      -> unique long-segment spawn anchors
                                      -> CityPedestrianDirector
                                         -> two local reusable actor/model slots
                                         -> runtime-random one-slot spawn events
                                         -> `1.25-7.5 s` first / `3.5-12.5 s` later delays
                                         -> fog-hidden `76-86 m` spawn band
                                         -> linked dense-fog fallback from `32 m`
                                         -> one-shot shortest graph approach to `24 m`
                                         -> ordinary random roaming after encounter
                                         -> player-distance recycling beyond `88 m`
                                         -> night cap `1`; `15-35 s` / `30-70 s` delays
                                         -> no camera/frustum lifecycle dependency
                                         -> safe CharacterController activation
                                         -> forward turns + 50% zebra choice
                                         -> shared Player Idle/Walk clips
                                         -> stable yield; no NPC/NPC collision
layout -> CityBusPlanner -> canonical right-hand Route 01
                         -> deterministic closed winding Street service loop
                            -> every actual district POI -> PlayerHome
                            -> default five target-owned stops
                            -> one ordered successor per occurrence; no random routing/pursuit
                            -> repeated physical links get unique occurrence IDs
                         -> sampled full-body clearance envelope
                            -> accepted straight + analytic `6 m` left-turn links
                            -> paired signal fixtures proven at `0.30 m` radius
                            -> selected Road v2.1 apron safe-right macro
                               -> S over full incoming Street -> `4.5 m` quarter arc
                               -> symmetric S over outgoing Street
                               -> owns both road edges; cannot bypass a stop edge
                            -> ordinary tight `3 m` rights remain rejected
                         -> Home frontage/one edge; river POI nearest same-district cycle (<=5 edges, <=120 m)
                            -> pole on another roadside cell outside target bounds
                            -> working pole near and outside Last Route Island
                            -> CityBusStopWorldBuilder -> physical blue `01` poles
                         -> fog-band spawn poses
                         -> CityBusDirector
                            -> one reusable actor/model slot
                            -> preferred hidden `76-86 m` activation
                            -> `56-86 m` fallback only with forward encounter path
                            -> outside-player/pedestrian yielding
                            -> attached hero omitted; pedestrian yielding retained
                            -> fixed `10 s` total stop dwell once per lap
                               -> `0.70 s` open + `0.70 s` close transitions
                               -> two double-leaf doors
                               -> CityBusDriverDoorTimeline deterministic sample
                            -> full-body recycling from `92 m`
                               -> forbidden while passenger owner remains attached
                               -> forced shutdown invokes registered passenger cleanup
                            -> no camera/frustum lifecycle dependency
                            -> CityBusActor kinematic box + engine
                            -> CityBusPresentation
                               -> grounded wheels + sprung `Suspension Visual` body
                                  -> heave `0.045 m` / pitch `0.8°` / roll `1°` caps
                                  -> actor/collider/route pose unchanged
                               -> inward door leaves + rotating steering wheel
                               -> dashboard door button with `12 mm` travel
                               -> CityBusDriverPresentation seated procedural IK
                                  -> both hands follow rotating wheel grips
                                  -> right hand presses button; left keeps its grip
                                  -> normal head + long eyes hold the open-door look
                                  -> nearby hero-head focus + capped rubber-neck stretch
                                  -> deterministic blink + exact pooled reset
                               -> night-factor emission
                               -> 2 headlight + 2 soft cabin runtime Spots
                                  -> sprung-body children; NightFactor/pool controlled
                            -> CityBusRideController
                               -> standard prompt at open front and rear passenger doors
                               -> street-surface-height door dock retained for exit
                               -> service hold through `BusBoardEnter`
                               -> moving pelvis target -> opposite-driver window seat `07`
                               -> original-hierarchy root late-syncs to actor-local seat
                               -> `BusRideLoop` + seat-following, world-level camera
                                  -> independent RMB mouse/right-stick yaw + pitch
                               -> exit only after a later service ordinal
                               -> service hold through `BusAlightExit`
                               -> independent grounded roadside exit + chase-camera blend
                               -> completion/cancel/shutdown restores player + ownership
                         -> CityMapBusOverlay
                            -> simplified blue ink-outlined closed route
                            -> five default numbered localized hover stops + compact legend
                            -> below orange player route; no live bus marker
five gameplay roots -> PlayerFactory -> Resources/Player/Player3D.prefab
                                      -> 73 mesh bindings + 16 core parts
                                      -> 29 Generic in-place Actions
                                         -> Idle/Walk/face/status/fall
                                         -> 50-frame full-body Rise via all fours
                                         -> BusBoardEnter/BusRideLoop/BusAlightExit
                                         -> ChessSeatEnter/ChessSeatPlayLoop/ChessSeatExit
                                      -> real URP mesh shadows
player -> PlayerContactShadow -> planted/fall-aware analytic patch
player -> PlayerInteractor -> InteractionPromptView -> same guarded Interact action
                         -> Route 01 front/rear door / fixed passenger seat
                            -> CityBusRideController board / later-stop exit
                         -> BarEntrance/BarExit -> SceneTransitionService
                         or SupermarketEntrance/Exit -> SupermarketInterior
                            -> matching City supermarket return
                         or HomeEntrance -> StairwellInterior
                            -> StairwellApartmentEntrance -> HomeInterior
                            -> HomeExit -> StairwellInterior
                            -> StairwellStreetExit -> City home return
                                                  -> DoorTransitionRoot
                                                     -> preloaded destination
       <- active-bar return spawn/context <- GameSessionState
       -> StairwellLayoutPlanner -> StairwellLayoutValidator
                                 -> StairwellWorldBuilder
                                    -> 48 visual steps + three physical ramps
                                    -> lower/middle/apartment landings
                                    -> sealed upper-flight debris
                                    -> StairwellDressingBuilder -> visible decay/clutter
                                    -> StairwellSurfaceAppearance
                                       -> cache 8 Resources albedo recipes
                                       -> native-UV _BaseMap_ST + surface MPB values
                                       -> shared RuntimePrimitiveLit + retained tint
                                       -> ordinary visible renderers only
                                          (not hidden ramps/blocker, tubes/halos,
                                           hero/cat or dust/VFX)
       -> StairwellInteriorAtmosphere -> three flickering practicals
                                      -> green grade + sparse dust
       -> StairwellFixedCameraController -> lower/middle/apartment hard cuts
                                         -> same world-oriented 3D hero
       -> StairwellCatPlan -> Middle Landing Back Rail perch + walkable approach
                           -> StairwellCatActor -> rear-view billboard
                                                   + player-tracking head
                                                   + ordinary idle
                                                   + rare 8-frame grooming (~36 s)
                                                   + prepared 16-frame feeding override
                           -> InventoryTargetInteractionController
                              -> Talk -> existing localized cat response
                              -> Interact -> OpenStewCan requirement
                                 -> missing feedback
                                 or default-No Feed confirmation
                                    -> prepare player/cat resources
                                    -> atomically remove one can
                                    -> grounded guided entry or clean cancellation
                                    -> neutral 3D render-frame settle
                                    -> CatFeedEnter/Loop/Exit + cat sprite track
                                    -> terminal 3D pose hold + separate exit pose
                                    -> exact modal/presentation restoration
       -> StairwellAmbiencePlayer -> steady concrete room bed
       -> StairwellSoundscape -> spatial ventilation + electrical buzz
                              -> seeded pipe/metal/water/movement cues
       -> HomeInteriorLayoutPlanner -> HomeInteriorLayoutValidator
                                    -> HomeInteriorWorldBuilder
                                       -> HomeBathroomBuilder
                                       -> HomeInteriorDressingBuilder
                                       -> HomeRefrigeratorPlan
                                          -> split counter + shifted table approach
                                          -> HomeRefrigeratorWorldBuilder
                                             -> hollow worn cabinet + eight slots
                                             -> vodka / egg / open stew can
                                             -> HomeRefrigeratorItemCatalog + ItemView
                                                -> localized metadata + tight triggers
       -> HomeBalconyLayoutPlanner -> HomeBalconyLayoutValidator
                                   -> window + open door + walkable safe balcony
       -> same blueprint ID + seed -> HomeExteriorContextPlanner
                    -> bounded roads/lots/windows/lamps/signals/decorations view
                    -> same Route 01 plan -> nearby PlayerHome-target stop
                       -> static collider-free blue `01` pole in Home space
                    -> same CityDecorationWorldBuilder recipes in Home space
                    -> shared City exterior appearance + passive bar facade
                    -> no second City root/player/camera
                    -> HomeExteriorPedestrianPlanner
                       -> same sidewalk/turn/zebra graph in Home coordinates
                       -> bounded `100 m` approach anchors beyond the facade
                    -> no ambient bus actor/director
                       -> visible default road terminal
                       -> no real Street pass-through with two `56 m` body-safe seams
                       -> no fabricated road or camera-owned visible pop
                    -> Balcony-only City visibility, fog field and light pool
                       -> exact City fog/background/48 m cap/current light/grade
                       -> at most 12 street/bar lights scaled by night factor
                       -> enable two distance-managed pedestrian slots only in shot
                       -> captured Home render state restored on exit/disable
       -> HomeInteriorAtmosphere -> two aligned practical Light/emitter/halo pairs
                                 -> synchronized cold shadowed bathroom-spill Spot
                                 -> shadowed time-of-day window cookie Spot
                                 -> warm entry-lamp Spot over door and floor
                                 -> at most five owned local realtime lights
                                    + separate scene Directional light
                                 -> shared transparent glass + grade + sparse dust
       -> HomeDayNightController -> window night/day blend
                                 -> Balcony current City lighting sample
                                 -> exterior fixtures scaled by night factor
       -> HomeAmbiencePlayer -> calm steady room bed
       -> HomeSoundscape -> synchronized closed/open refrigerator loops
                          -> equal-power crossfade from current door amount
                          -> spatial balcony night air
                          -> seeded wood/radiator/radio/bathroom cues
                          -> bathroom-tube crackle on applied flicker changes
       -> HomeMusicPlayer -> optional home_theme -> unscaled fade envelope
                          -> Balcony fade-out + pause
                          -> indoor same-sample resume + fade-in
       -> HomeFixedCameraController -> main/bath/balcony activation + hold bounds
                                     -> PlayerCameraFollow fixed pose
                                     -> same world-oriented 3D hero in every shot
       -> HomeOcclusionRegistry -> furniture/dressing/door/rail renderer groups
                                -> HomePlayerOcclusionController
                                   -> five samples from combined 3D renderer bounds
                                   -> head/chest/pelvis rays trigger group cutaway
                                   -> shared dither fade / hold / restore
                                   -> full opacity during Home modal presentation
       -> HomeBedInteractionPlan -> reachable door-side trigger + seated waypoint
                                 -> HomeBedInteraction -> first/second E
                                    -> PlayerAnimatedInteractionController
                                       -> visible Positioning -> Entering/Looping/Exiting
                                       -> separate root/pelvis/facing entry + exit poses
                                       -> held seated pelvis waypoint on both transitions
                                       -> BedEnter/BedSleepLoop/BedExit on same rig
                                       -> sample then align registered pelvis anchor
                                       -> grounded guided walk/turn or stalled cancel
                                       -> neutral 3D rendered settle frame
                                       -> terminal clip pose -> independent exit pose
                                       -> normal completion -> session fatigue reset
                                          + clear fatigue fraction
                                       -> owner/transition cancel -> complete restoration, fatigue preserved
       -> HomeBalconySmokingPlan -> entry/exit dock at (6.60, 0.04, -1.45)
                                   -> permanent rail ashtray at (7.25, 1.12, -1.67)
                                      -> visual-only dish under exit-flick ember
                                   -> first E -> guided walk + visible face toward city +X
                                      -> neutral 3D render-frame settle
                                      -> SmokeEnter -> retrieve + first held inhale
                                      -> held SmokeLoop -> lift + inhale + lower + outward exhale
                                      -> 74 mm cigarette along SOCKET_Cigarette.R +Y
                                      -> world-space plume from SOCKET_Mouth +Y
                                         -> bounded burst at loop-local frame 16
                                         -> cityward growth/fade before next 9.5 s loop
                                   -> second E -> queued calm-boundary exit
                                      -> SmokeExit -> rail flick + empty-hand terminal pose
                                      -> independent exit-root restoration
                                   -> continuous mesh/contact shadows
                                  -> quadratic city-biased push to 38-degree FOV
                                     -> 0.33 m Home-local +X look offset
                                     -> hero near 0.37 viewport X; city visible right
                                     -> local 13-23 s harmonic camera drift
                                     -> no FOV pulse; continuous phase clock
                                  -> drift fades to zero with exact shot restoration
                                  -> optional smoking_theme fade in/out
       -> HomeRefrigeratorInteraction -> modal unscaled timeline
                                      -> clickable close prompt -> RequestClose
                                      -> first-person Bezier camera
                                         + prefab-derived right-arm subset
                                         + owner-scoped visibility lease
                                      -> seal / handle / 102-degree door animation
                                      -> persistent lit inspection
                                      -> HomeRefrigeratorItemInspectionController
                                         -> hover tint + localized cursor name
                                         -> Browsing/FlyingIn/Inspecting/FlyingOut
                                         -> centered slow rotation + dark backdrop
                                         -> name/description + Take/Use/Back placeholders
                                         -> exact transform/collider/color restoration
                                      -> close + exact fixed-shot/player restoration
                                      -> HomeSoundscape equal-power hum crossfade
       -> HomeAlarmClockPlan -> HomeAlarmClockBuilder
                             -> silent clock/nightstand room dressing
                             -> reusable flickering 05:59 / Wake-only solid 06:00
                             -> then persistent session HH:MM
                             -> HomeAlarmClockSynthesis -> spatial SFX/World ring
       -> consumed HomeArrival.OpeningSleep -> HomeOpeningController
                                             -> direct sleeping loop + modal lock
                                             -> 5 s locked flickering 05:59 shot
                                             -> silent 05:59 + Wake Up/Quit
                                             -> Wake -> solid 06:00 + start time + 3 s ring
                                             -> ring stops -> wake + smooth camera arc
                                             -> 2x exit + continuous gameplay settle
                                             -> existing wake frames
                                              -> normal Home camera/input, no handoff cut
       -> HomeDebugCityMapShortcut -> F9 from any Home phase
                                   -> direct City load + PlayerHome return
                                   -> DebugCityMapOnArrivalRequested
                                      -> City waits for transition completion
                                      -> enable teleport + open map + clear request
       -> BarInteriorLayoutPlanner -> BarInteriorLayoutValidator
                                   -> BarInteriorWorldBuilder
                                   -> seven zones + four clear paths
                                   -> practical light/audio/NPC anchors
       -> BarInteriorAtmosphere -> six shadowless lights + grade + dust
       -> BarPatronWorldBuilder -> pooled 3D guests seated/standing on anchors
       -> BarSoundscape -> spatial crowd bed + rare bar cues
       -> BarArrivalPresentation -> skippable Bezier camera reveal
        -> BarCounterStation -> BarDrinkShop
                            -> retail catalog + atomic cash/drink transaction
                            -> BarDrinkServicePlan -> nine physical bottle slots
                            -> BarDrinkServiceWorldBuilder
                               -> 9 bottle views + 5 vessel views + pour stream
                            -> BarDrinkServiceTimeline
                               -> seated camera + prefab-derived arm subsets
                               -> owner-scoped world visibility lease
                               -> pickup -> pour -> 3 s drink -> vessel return
                               -> persistent browser -> explicit camera exit
                             -> GameSessionState wallet + drinking progress
       -> SupermarketInteriorLayoutPlanner -> validated 16x11x3.6 shop
                                             -> three physical shelf views
                                                -> noodles + day-old loaf
                                                -> vodka + closed stew
                                                -> chicken egg
                                             -> decorative checkout + Watcher Cashier
                                                -> pursuit curve -> head hovers by the hero
                                                -> arcs over shelves, never clips
                                                -> caught-looking retract + stare
                                                -> E talk stub -> placeholder line
                                             -> four corner CCTV heads track the hero
                                             -> six practicals + one flickering row
       -> SupermarketShelfStation -> product-centered authored shelf camera
                                   -> cyclic stocked-shelf navigation
                                      -> muted clickable arrows beside product
                                      -> pointer/keyboard/gamepad shared action
                                   -> SupermarketPurchaseRules
                                      -> atomic cash + inventory + source commit
                                      -> remove physical product immediately
                                      -> filter source on scene re-entry
GameSessionState intoxication -> IntoxicationStageRules
                              -> motor + 3D status bones + camera
                              -> IntoxicationRenderState -> PS1 world composite
                              -> above 60 -> balance scheduler/model
                                 -> BalanceCheckView
                                 -> success or Fall clip -> bounded ragdoll
                                    -> one 50-frame Rise phase via all fours/crouch
F9 -> MinigameDebugWindow -> Left/Right arrows or buttons -> intoxication +/-20
                          -> City test-teleport toggle -> CityMap all-lot selection
                                                       -> Yes -> PlayerMotor.Teleport
Home F9 -> HomeDebugCityMapShortcut -> City at home -> open debug-teleport map
F8 -> GameDiagnosticsSnapshot -> GameLog -> flushed debug.log state record
state boundaries + scene correlation -> GameLog -> rotating NDJSON
Unity warning/error/exception ----------------------------^
scene root -> GameAudioMixer -> City/Bar/Stairwell/Home/DoorTransition snapshot
scene transition -> preload -> outgoing theme fade-out -> activate destination
City root -> CityMusicPlayer -> city_theme + entry/exit fades ----> Music
Bar root -> BarMusicPlayer -> bar_theme + entry/exit fades ------> Music
Supermarket root -> SupermarketMusicPlayer -> optional supermarket_theme + fades -> Music
Stairwell root -> StairwellMusicPlayer -> optional stairwell_theme + fades -> Music
Home root -> HomeMusicPlayer -> optional home_theme + Balcony pause/resume -> Music
Home smoking interaction -> optional smoking_theme + gain envelope -> Music
scene root -> matching procedural ambience -----------------------> Ambience/Beds
Home/Stairwell root -> spatial soundscape ------------------------> Ambience/Details
Home opening -> HomeAlarmClock -> spatial mechanical ring --------> SFX/World
input/gameplay events -> RetroAudioService -> pooled SFX/UI groups
Music/details/world sends -> reverb/echo returns -> Master compressor
URP post-processing -> 640x360 average -> subtle RGB555 blend -> point upscale
world composite -> crisp retro IMGUI overlay
```
