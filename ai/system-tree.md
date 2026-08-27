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
    StairwellInterior.unity
    HomeInterior.unity
    MountainRoad.unity
    AreaLoading.unity
    ChurchInterior.unity
  Settings/
    CityNoirVolumeProfile.asset
    PCPresentationBaselineVolumeProfile.asset  project-owned Neutral/Bloom/Vignette baseline
    PC_RPAsset.asset              active PC pipeline + baseline volume reference
    PC_Renderer.asset             active PC PS1 renderer feature
  Resources/
    Materials/
      CityNoirEmission.mat
      HomeOccluderDither.mat       shared opaque Home foreground cutaway
      Ps1Composite.mat
      RuntimePrimitiveLit.mat      shared packaged URP/Lit runtime geometry
    Textures/
      CityGroundSoilAlbedo.png     generated compacted-soil ground; 512 runtime, Repeat/mips
      CityFringeServiceTrackAlbedo.png measured compacted aggregate; 512 runtime, Repeat/mips
      CityFringeConcreteAlbedo.png measured board-formed concrete; 512 runtime, Repeat/mips
      CityFringeMasonryAlbedo.png measured old irregular masonry; 512 runtime, Repeat/mips
      CityFringeForefieldAlbedo.png quiet compacted fill for full mountain forefields; 512 runtime, Repeat/mips
      CityMountainRockAlbedo.png   deterministic weathered rock; 512 runtime, Repeat/mips
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
      CityLighthouseBeam.shader   additive no-fog lantern beam/lens, distance self-fade
      CityLighthouseIsland.shader fixed-haze island silhouette, distance self-fade before the far plane
      CityMountainBackdrop.shader camera-relative west/south silhouette inside the 48 m cap
      CityMountainPhysical.shader shared opaque ridge handoff, fog floor + matching depth passes
      CityRiverWater.shader      quantized animated river flow with night/rain response
      HomeOccluderDither.shader   Forward+ grouped cutaway with shadow/depth/normals
      HomeWindowGlass.shader      shared transparent Home window/door glass
      StairwellCatGrin.shader     arc-length reveal of the Cheshire grin, shader teeth seams
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
      StairwellCatProvider.asset      serialized link to the passive 3D cat prefab
    City/
      YardWheelchairProvider.asset  serialized link to the staged yard rider prefab
      CityMiscAssetProvider.asset   186 passive City role-mesh bindings + manifest signature
      CityBuildingAssetProvider.asset  four district prototype-prefab bindings + signature
      Buildings/
        OldTownPrototype01.prefab      passive fixed-metre wrapper + semantic registry
        ResidentialPrototype01.prefab passive fixed-metre wrapper + semantic registry
        IndustrialPrototype01.prefab  passive fixed-metre wrapper + semantic registry
        NightlifePrototype01.prefab   passive fixed-metre wrapper + semantic registry
    Bar/
      BarFacade3D.prefab            complete fixed-metre bar_exterior_v2 + door/sign anchors
      BarInterior3D.prefab          passive shared 22 x 16 x 4.8 m room model
      Textures/                     four interior albedos + exterior brick/plaster sheets
    MountainRoad/
      MountainRoadCafeCastProvider.asset  four isolated staged cafe prefab links
    Church/
      ChurchExterior3D.prefab          passive Catholic exterior + typed semantic anchors
      ChurchInterior3D.prefab          passive furnished interior + typed semantic anchors
    Localization/
      ru.json
      en.json
  Player3D/
    Models/
      PlayerCharacter3D.fbx             production Generic model
      PlayerCharacter3D.json            deterministic parts/bones/actions manifest
    Animations/
      PlayerCharacter3DAnimations.fbx   37 in-place Actions; Rise, bus, car-door, chess-seat + door-use sets
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
      MountainRoadCafeCast.{fbx,json}   isolated eight-clip silent cafe cast library
    Staged/
      Models/
        PipebackRoller3D.fbx            passive 31-bone wheelchair NPC model
        PipebackRoller3D.json           staged geometry/rig/passive-anchor manifest
        MountainCafe*3D.{fbx,json}      four distinct passive cafe role models/manifests
      Prefabs/
        PipebackRoller3D.prefab         passive asset outside Resources and the runtime pool
        MountainCafe*3D.prefab          four cafe roles outside Resources/pedestrian pool
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
      CityMisc3D.fbx                    94 citywide misc assemblies / 186 role meshes
      CityMisc3D.json                   roots, roles, bounds, compatibility + build signature
      CityBuildings3D.fbx               four fixed-metre district prototypes / 24 role meshes
      CityBuildings3D.json              envelopes, attachments, window slots + build signature
  Bar/
    Models/
      BarFacade3D.fbx                   38-part complete old-neighbourhood pub exterior
      BarFacade3D.json                  bar_exterior_v2 bounds, parts, door/sign anchors + signature
      BarInterior3D.fbx                 156-part shared interior
      Bar3D.json                        interior layout/parts/groups + build signature
  Church/
    Models/                             split Catholic exterior/interior FBX + shared manifest
    Textures/                           nine deterministic plaster/stone/wood/glass/art sheets
    Materials/                          shared URP bindings for the thirteen semantic slots
  Stairwell/
    Cat/
      Models/
        StairwellCat3D.fbx              armature-free pivot-empty Cheshire cat
        StairwellCat3D.json             deterministic parts/pivots/grin-UV manifest
      Prefabs/
        StairwellCat.prefab             passive asset outside Resources
  Scripts/
    Runtime/
      Core/          ten-scene bootstrap, gameplay roots, session, transitions
        CityGameRoot.cs           city composition + deferred debug-map arrival
        AreaTravelTypes.cs        stable City/MountainRoad IDs + arrival token
        AreaTravelService.cs      guarded Single-load area handoff through AreaLoading
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
        CitySurfSound.cs                   one nearest-waterline spatial surf voice
        CityThunderSound.cs                deterministic azimuthal thunder + distance delay
        CitySourceSoundSynthesis.cs        quantized City fixture loops/details/actions
        CitySoundSourceDescriptor.cs       physical-owner anchor and causal-mode contract
        CitySoundscapePlan.cs              stable-ID ordered loop/scheduled/triggered views
        CitySoundscapePlanner.cs           pure validation, profiles and stable hashes
        CitySoundSchedulePlanner.cs        deterministic no-catch-up detail cursors
        CitySoundscapeAnchorPlanner.cs     POI/fountain plans -> exact physical anchors
        CitySoundOcclusion.cs              coarse authored-building mass attenuation
        CitySoundscapeDirector.cs          bounded spatial pool + real-action bindings
        CityTunnelLampSoundSynthesis.cs    faulty-lamp mono ballast/crackle PCM
        MountainRoadSoundscape.cs          five causal positioned mountain emitters
        MountainRoadCafeSoundscape.cs      three visible-appliance cafe voices
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
        CityBlueprintCatalog.cs  default 13x12 river city with eastern Cemetery, six Yards + legacy blueprint
        CityRiverPlan.cs         10 m channel, dual core promenades, three typed bridges + four lower landings
        CityRiverResources.cs    shared animated water material and night/rain factors
        CityRiverWorldBuilder.cs core river + mountain-plan cave water/bed/banks/rails + always-lit wall lamps
        CityElevationPlan.cs     node/cell datums, classified grades + authoritative height sampler
        CityElevationPlanner.cs  river-valley node profile + flat custom fallback
        CityElevationValidator.cs coverage, water, grade + four-district stair invariants
        CityElevationRebaser.cs  canonical lots, park, POIs, surfaces + access anchors
        CityTerrainSurfacePlan.cs continuous Buildable/Park/Open/Beach top and normal sampler
        CityTerrainSurfaceWorldBuilder.cs triangulated/filterable terrain batches + matching mesh colliders
        CityMountainBoundaryPlan.cs west/south ridges, fenced SW ground, river-cave terminus + open tunnel path
        CityMountainBoundaryPlanner.cs terrain/toe-sampled boundary, cave approaches + corner-ground infill
        CityMountainBoundaryValidator.cs side/cave/tunnel/corner geometry + natural-ground invariants
        CityMountainBoundaryMeshFactory.cs ridges with buried toe bonds + shared render/collider corner earthwork
        CityMountainBoundaryWorldBuilder.cs corner ground, ridges, river-cave rock stop + uncapped bent tunnel lining
        CityMountainSurfaceAppearance.cs rock recipe + one shared fog-safe physical-ridge material
        CityMountainBackdrop{Resources,WorldBuilder,Follower}.cs camera-relative closed west/south shell only
        CityFringeYardPlan.cs     five typed Yard profiles, parts, practicals + open forecourt contract
        CityFringeYardPlanner.cs  terrain-graded service belt, grounded tunnel returns + low east utility edge
        CityFringeYardForefieldPlanner.cs road/middle/toe bands + 3-4 seeded meso anchors per mountain strip
        CityFringeYardGroundWorldBuilder.cs exact generic/forefield terrain split with one collider per source area
        CityFringeYardRetainingPlanner.cs precise retaining cuts around rock-access corridors
        CityFringeYardLandmarkPlanner.cs four macro anchors incl. tunnel service frame + crown floodlight
        CityFringeYard{PracticalPlan,PracticalValidator}.cs four clear, deterministic practicals
        CityFringeYardSurfaceAppearance.cs four measured shared surface families
        CityFringeYardValidator.cs bands/gaps/all-safe-seams/corridors/vocabulary/budget invariants
        CityFringeYard{WorldBuilder,WorldResult}.cs imported utility/shed/gauge shells + Unity terrain, cables and practicals
        CityFringePracticalAnchor.cs runtime pose passed to the fixed night-light pool
        MountainRoadPlan.cs       typed 620 m route, ten hairpins, bridge, tunnel/plateau + dressing
        MountainRoad{Planner,Validator}.cs route/bridge + ridge envelope/footprint invariants
        MountainRoadBridge{WorldBuilder,Validator}.cs deck/beams/piers/open rails + bounded physics
        MountainRoadTerminal{Plan,Planner,Validator}.cs vehicle/cafe/cableway terminal contract
        MountainRoad{Terrain,Surface,Scenery}*.cs 76 m terrain, gorge, road + colliderless terminal apron
        MountainRoadSurfaceAppearance.cs six printed + nine borrowed measured surface families
        MountainRoadCafe{WorldBuilder,WorldResult,Geometry}.cs enterable glass cafe
        MountainRoadCafeCast{Plan,Provider,AssetRegistry,Factory,Presentation,Controller}.cs four-role silent cast
        MountainCableway{Motion,Controller,WorldBuilder}.cs continuous cabins + causal machinery
        MountainRoadMiscAssetProvider.cs 19 passive Blender meshes + deterministic visual variants
        MountainRoadWalkableArea.cs route/plateau movement boundary
        MountainRoadWorldBuilder.cs separate mountain-only composition + 12 imported misc batches
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
        CityWorldBuilder.cs      continuous terrain, fenced corner infill, river/bridges, graded streets, stairs + guarded drops
        CityBuildingPrototypePlacement.cs fixed-metre front/roof/facade poses + Home half-space classification
        CityBuildingPrototypeWorldBuilder.cs ordinary-lot Blender composition + foundation/collider authority split
        CitySpecialBuildingWorldBuilder.cs supermarket/home shells + inset textured pub foundation/collision + Home projection
        CityBuildingWindowSlotAppearance.cs UV2-addressed district window state binding
        HomeYardSitePlan.cs      shared roadless-gap, rider-ring, neighbour-light + leaning-utility geometry
        CityOpenAreaDecorationPlan.cs  deterministic inter-building bar-side yard/light descriptors
        CityOpenAreaWorldBuilder.cs    imported yard props/fixture shells + Unity collision, lens, halo and fixed Spot
        CityCemeteryPlan.cs      oriented cemetery part/lamp descriptors, six grave variants + bounded budget
        CityCemeteryPlanner.cs   gate-framed alleys, hash-varied graves/оградки, trees, lamps + validation
        CityCemeteryWorldBuilder.cs  imported graves/vegetation/furniture + Unity collision, lamps and grave-work dynamics
        CityCemeterySurfaceAppearance.cs  four cemetery albedos (granite/stone/gravel/soil) via MPBs
        CityCemeteryGroundWorldBuilder.cs  the cemetery slab, rebuilt around every open grave
        CityCemeteryGroundExcavation.cs   the register of open holes; cut and fill both rebuild the slab
        CityCemeteryPitWorldBuilder.cs    collar, floor, spoil heap + the cap that keeps the hero out
        CityHandLampWorldBuilder.cs      the shared kerosene hand lamp: pier head and graveside, one fixture
        CityCemeteryCoffinWorldBuilder.cs  six-sided turned-board coffin, overhanging lid, cross
        CityCemeterySealedGraveWorldBuilder.cs  turned mound courses + one planner monument, slab omitted
        CityChurchPlan.cs        `4 x 2` precinct, west door/approach, cemetery clearance + return
        CityChurch{Ground,World}WorldBuilder.cs typed ground and Catholic exterior composition
        ChurchAssetRegistry.cs   passive Blender-part/material/anchor contract shared by both prefabs
        ChurchResources.cs       typed Resources load/instantiate bridge for exterior and interior
        ChurchInteriorLayout{Plan,Planner,Validator}.cs Catholic zones, routes and exact furnishings
        ChurchInteriorWorld{Builder,Result}.cs imported interior plus plan-owned collision
        CitySeacoastPlan.cs      oriented coast part/lamp descriptors, four hull variants, frame + budget
        CitySeacoastPlanner.cs   zoned shore (port/esplanade/wild), mol, boat station, footbridge, mouth banks + validation
        CitySeacoastWorldBuilder.cs  imported boats/barge/oars/barrier/driftwood + Unity sea, infrastructure and fixtures
        CitySeacoastSurfaceAppearance.cs  five coast albedos (sand/concrete/granite/plank/hull) via MPBs
        CityLighthouseIslandPlan.cs      island part kinds/descriptors, budget, lantern position
        CityLighthouseIslandPlanner.cs   the offshore island: mound, ruined shacks, wreck, banded tower + validation
        CityLighthouseIslandMeshFactory.cs  one baked vertex-coloured mesh (24 verts/box) + the beam cone
        CityLighthouseIslandWorldBuilder.cs  silhouette mesh + un-batched lantern (lens, two beam cones)
        CityLighthouseIslandResources.cs  the two shared no-fog materials (silhouette haze, additive beam)
        CityLighthouseLanternController.cs  the rotating lantern: pure azimuth/flash rules + night gating
        CitySeaResources.cs      the sea material: zero flow, long swell, isotropic ripple, pier-lamp glint
        CityWaterResources.cs    the one night/rain drive shared by every water material
        CityParkSurfaceAppearance.cs  eight park albedos (lawn/path/plaza/bark/foliage/timber/stone/painted metal) via MPBs
        CityParkBenchPlanner.cs  four path-aligned ordinary benches per park region
        CityDistrict.cs          area IDs, district/path/land-use enums and park data
        CityTravelDistance.cs    weighted road/park-path distance between bars
        CityDistrictPointOfInterestPlan.cs  kinds, public bounds and street accesses
        CityDistrictPointOfInterestPlanner.cs  primary/public reservations + 18 m guard
        CityDistrictPointOfInterestWorldBuilder.cs  four imported static shells + Unity paving/collision, cloth, lights, mechanisms and NPCs
        CityDistrictArtProfile.cs  pure frontage/mass/window/light/wear identity for four urban districts
        CityDistrictPresentationPlan.cs  immutable per-block channel decisions + transition motif
        CityDistrictPresentationPlanner.cs  stable block keys and one-block allowed-neighbour transition band
        CityPointOfInterestSurfaceAppearance.cs  five scripted POI albedos (paving/metal/cloth/timber/paper) via MPBs
        CityDecorationDescriptor.cs  24 visual families and anchor contracts
        CityDecorationPlan.cs        immutable ordered seeded decoration data
        CityDecorationPlanner.cs     primary landmarks, lot visuals, tiers, clear clusters + spaced booth/dumpster coverage incl. bar-side yard pair
        CityDecorationValidator.cs   landmark/core quotas, IDs and clearances
        CityMiscAssetProvider.cs     64 kinds / 94 assemblies / 186 passive role meshes with roots and bounds
        CityBuildingAssetRegistry.cs fixed envelope, frontage, role, attachment + window-slot contract
        CityBuildingAssetProvider.cs four passive Resources prefab bindings used by City/Home builders
        CityDecorationWorldBuilder.cs  imported role batches, Unity collision proxies + utility dock read-back
        CityStreetUtilityDock.cs     booth-door/dumpster-lid docks the interactions stand on
        CityStreetUtilityWorldBuilder.cs  one placeholder trigger per utility dock
        CityBoardGamePlan.cs     playable tables, seated eye pose + board-plane square picking
        CityBoardGamePieces.cs   live men on one table: pooled views, carries, sweeps and crowns
        CityBoardGameMarkers.cs  pooled emissive plates for hover/selection/destinations/check
        CityChessSetMen.cs       the four static batches, remembered per table so one can be hidden
        CityStaticCollisionBuilder.cs  tier catalog + decoration/park/pole box proxies
        CityExteriorAppearance.cs    shared City/Home wet-aware surfaces + district window family resolver
        CityWetSurfaceRegistry.cs    cross-scene rain film, slow drying + tint-preserving MPB response
        CityPuddlePlanner.cs         deterministic bounded road-patch plan
        CityPuddleWorldBuilder.cs    one collider-free top-only puddle mesh
        CityWindowAppearance.cs      windowed-pane sheet, five shared lit materials on the night factor
        CityNightGlowRegistry.cs     registered electric glows (neon/signs/lamps) that die by day
        CityNightSiteLightRegistry.cs  authored site realtime lights scaled/disabled by the night factor
        CityFacadeGrid.cs            single source of the bay/floor pitch both walls and windows read
        CityFacadeAppearance.cs      district wall albedos tiled by that grid, not by metres
        CityBarFacadeWorldBuilder.cs complete fixed-metre pub exterior + preserved door/sign anchors
        BarExteriorSurfaceAppearance.cs dedicated brick/plaster/roof sheet binding
        CitySupermarketFacadeWorldBuilder.cs  shared branded supermarket storefront
        SupermarketEntranceGeometry.cs  frontage, apron and fence-opening dimensions
        RoadFencePlan.cs         MapBoundary/DeadEnd/CornerGuard rails + clearance-opening metadata
        RoadFencePlanner.cs      unsupported edges, true Street terminals + default NE road-cap L
        CityNightFixturePlanner.cs  lamps/signals clear public ground and approaches
        CityNightWorldBuilder.cs imported street-lamp/signal shells + Unity bulbs, halos, controllers and Lights
        CityDayNightController.cs   session lighting + exterior night factor
        CityWeatherController.cs    per-frame weather sample -> rain, wet film, flash, thunder
        CityRainField.cs            seeded player-following stretched rain streaks
        CityTunnelLightingController.cs five path fixtures, pooled faulty Spot + local sound
        CityTunnelShelterController.cs  portal hysteresis -> fog/backdrop state + shelter provider
        CityLightningFlashLight.cs  transient shadowless directional storm flash
        CityRopeSpanGeometry.cs     shared parabolic rope sag: curve samples + chord-chain boxes
        CityWindDressingPlan.cs     cloth/rope prop descriptors, per-zone budgets, 32-piece cap
        CityWindDressingPlanner.cs  cross-zone pass hanging cloth off other plans' drawn anchors
        CityWindDressingValidator.cs  budgets, unique ids, water/approach clearance for poles
        CityWindDressingWorldBuilder.cs  batched poles/rope chords + per-piece wind-registered cloth
        RoadWalkableArea.cs      ground/road/promenade union + bounded open-tunnel corridor
        HomeInteriorLayout*.cs   main/bath paths, nine footprints and corner blocker
        HomeOcclusionRegistry.cs explicit logical renderer groups and visibility floors
        PlayerHomeBalconyGeometry.cs  shared City/Home facade transform and dimensions
        HomeBalconyLayout*.cs    connected room/threshold/deck walkable plan
        HomeExteriorContextPlan.cs  bounded street/decoration/pedestrian + Home-stop context
        HomeBalconyWorldBuilder.cs   window, open door, deck, safe rails + permanent ashtray
        HomeExteriorViewBuilder.cs   collider-free lots/windows/lights + static Home stop
        HomeBedDeformableSurfaceFactory.cs  grid-top mattress/pillow meshes + data component
        HomeBedInteractionPlan.cs  open-side trigger + separate entry/action/exit poses
                                   -> hip heights measured off the mattress, not guessed
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
      Stairwell/Cat/ 3D Cheshire trickster: perch, pivot articulation, grin API
        StairwellCatPlan.cs                 rail-contact perch + walkable approach
        StairwellCatProvider.cs             one addressable link to the passive prefab
        StairwellCatFactory.cs              instantiation + passive-presentation guard
        StairwellCatRigAnchors.cs           pivot/renderer bindings; asset metadata only
        StairwellCatActor.cs                adopts pivots, articulates idle/feeding/grin
        StairwellCatIdleModel.cs            untouched deterministic idle timeline
        StairwellCatHeadYawModel.cs         continuous hysteresis head tracking (65 cap)
        StairwellCatPoseRules.cs            pure frame -> pivot-delta mapping
        StairwellCatGrinTimeline.cs         pure appear/hold/vanish arc (0.4s/1.2s)
        StairwellCatGrinController.cs       public BeginGrin/EndGrin API, MPB progress
        StairwellCatFeedingPlan.cs          safe middle-shot entry/action/exit poses
        StairwellCatFeedingTimeline.cs      16-step, 6 fps one-shot feeding contract
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
      Seacoast/      the boat station's one permanent inhabitant
        SeacoastFishermanProvider.cs  the only serialized reference to the staged prefab
        SeacoastFishermanPlan.cs  stance, facing and waterline read back from the coast plan itself
        SeacoastFishermanQuips.cs  15-line seeded repertoire, never twice running, never second person
        SeacoastFishermanInteraction.cs  talk stub on a trigger docked behind him, not in front
        SeacoastFishermanPresentation.cs  single-clip manual PlayableGraph; publishes the loop's breath phase
        SeacoastFishermanRigAnchors.cs  bind-pose anchors for the pipe bowl and the rod point
        SeacoastFishermanPipeEffect.cs  ember, its point light and the plume, all on the breath phase
        SeacoastFishermanLine.cs  line struck from the live rod tip to the sea's own top
        SeacoastFishermanFactory.cs  one staged instance, passivity validated, magnet + talk trigger + pipe + line
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
        CemeteryGravediggingRegister.cs  every grave he has going at once + the next offer
        CemeteryGraveWorkLedger.cs  the book of work: one rung and one epitaph per plot
        ICemeteryWorkGiver.cs  what the watchman's window speaks for: one grave or the yard
        CemeteryGraveDigSiteInteraction.cs  one staged worksite stub: dig, lower, fill, set
        ICemeteryGraveWorkSession.cs  the seam that lets an act be earned instead of pressed
        CemeteryGraveWorkController.cs  the modal session: camera, hero lease, input, commit
        CemeteryGraveWorkStance.cs  where he stands, which way he throws, what the shot sees
        CemeteryGraveLatticeModel.cs  6 x 3 courses and the no-pillar rule that is the whole act
        CemeteryStrokeModel.cs  the swing bar, and the one rule that judges a strike
        CemeteryCoffinLowerModel.cs  two ropes, tilt and the slip that drops the box
        CemeteryStoneSettleModel.cs  heaving it upright, then three blows to set it
        CemeteryEpitaph.cs  the eight words a plaque holds, counted and cut
        CemeteryPlaqueReadInteraction.cs  the finished grave's board, read again
        (World) CityCemeteryPlaqueWorldBuilder.cs  the board, fitted to the stone's measured face
        (World) CemeteryPlaqueFont.cs  a runtime TMP font asset off a Cyrillic .ttf
        (World) CemeteryPlaqueSurface.cs  the board's three lines, reset when the line is cut
        CemeteryShovelAnimator.cs  drive, lever, lift, dump — the only animated thing here
        CemeteryGraveSlings.cs  four slings over the open mouth, and the coffin pose they imply
        (World) CityCemeteryCoffinRestWorldBuilder.cs  the two blocks the box waits on
        (World) CityGravediggerShovelWorldBuilder.cs  the spade, and where it stands between acts
        (World) CityCemeteryProgressivePitWorldBuilder.cs  the half-dug hole, one earth block per segment
        (World) CityCemeterySegmentFrameWorldBuilder.cs  the outline round the square the spade is aimed at
      Park/          the two old men's boards, once somebody sits down at one
        CityBoardGameController.cs  seat hookup, seated camera ownership, pointer/cursor input, opponent think clock + every board state as a spoken cue
      LastRoute/     the island's car, the one man waiting by it, and the journey out
        LastRouteCarPlan.cs        where the car stands, off the paving and clear of every way in - or anywhere at all, once it has driven
        LastRouteCarFactory.cs     the staged car, its doors, springs, halos and the passenger seat
        LastRouteCarDoors.cs       leaves that swing on their own hinges, and the swing-clearance rule both docks obey
        LastRouteCarSuspension{,Model}.cs  the body on springs, kicked by dismounts and seatings
        LastRouteCarSeatPlan.cs    the hero's dock, doorway waypoint and seated hip, all off drawn anchors
        LastRouteCarSeatViewPlan.cs  the seated eye, its look limits and the level-horizon rule
        LastRouteCarSeatInteraction.cs  the offer, the clip-driven passenger leaf and first-person camera ownership
        LastRouteFerryman{Plan,Factory,Provider}.cs  the one authored man, read off the car that was actually placed
        LastRouteFerrymanPresentation.cs  five postures on one manual graph, and the metres the clips do not carry
        LastRouteFerrymanBoarding{Plan,Timeline}.cs  the drop, the walk round the nose and the door-open-sit-shut clock
        LastRouteFerryman{Coin,Coat,RigAnchors,Quips,Interaction}.cs  the toss, the hem, the sockets, the twelve lines and the one question
        LastRouteFerrymanAlightingTimeline.cs  the same three beats run backwards, to get him out at the far end
        LastRouteFerrymanRideStage.cs  the monotone ladder both areas build him from
        LastRouteCarDrive{Path,Model}.cs  one drivable centreline with the one place on it the car gives way, and how fast a car will take it - corners, the end of the road and a stop line all braked to the same way
        LastRouteCarDriver.cs      the engine on the runtime root: pose, steering, wheel roll and what the road does to the springs
        LastRouteCarHeadlights.cs  two shadow-casting beams and a wide spill on the SPRUNG body, emitting from OUTSIDE the shell just proud of the lit face, switched by the journey itself; the area they light keeps its ordinary grade
        LastRouteCarGiveWay{,Model}.cs  wait or go at the turn across the road: the pure clock and commit rule, and the live look for an oncoming bus or anyone walking over the mouth
        LastRouteCityDeparturePlanner.cs  the lot exit, a Dijkstra over the layout's own street edges (NEVER the bus graph - that is Route 01's one-way loop), the turn off where the forecourt opens, and the run into the tunnel
        LastRouteMountainDrivePlanner.cs  the climb read out at a metre, from inside the tunnel to the middle of the apron
        LastRouteRideController.cs  who owns the beat: seat, engine, blackout, area load, arrival, the F10 skip and the man getting back out
      Vehicles/      one-slot real-scale Route 01 bus, passenger ride and presentation
        CityBusPlan.cs             immutable ordered Route 01 loop, target-owned stops + occurrences
        CityBusPlanner.cs          grade-safe Street graph, 3D samples + full-body clearance proof
        CityBusTargetRoutePlanner.cs grounded POI/Home candidates + deterministic winding loop solver
        CityBusWideTurnPlanner.cs  level-apron two-edge safe-right macro between graded links
        CityBusActor.cs            3D fixed-loop motion/pitch, 10 s dwell + service ownership
        CityBusAudio.cs            visibility-bounded rear diesel, passenger cabin layer + two causal doorway pneumatics
        CityBusDirector.cs         fog spawn, passenger-safe recycle and forced-cleanup lifecycle
        CityBusRidePlan.cs         local-surface door transfer + level camera geometry
        CityBusRideController.cs   prompts, board/ride/alight, ride look input + exact cleanup
        CityBusStopWaitPlan.cs     per-stop pavement wait slots + stop-seeded graph distances
        CityBusStopWaitPlanner.cs  locally grounded slot geometry + single-source Dijkstra
        CityBusNpcPassengerController.cs ambient waiters, seated boarding, random alighting
        CityBusStopWorldBuilder.cs imported Route 01 shelters/poles + Unity collision and Home-local placement
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
        PlayerFactory.cs           shared prefab spawn in all seven gameplay roots
        PlayerAttention.cs         Silent Hill head: notice cone rules, target picker + magnets
        PlayerCameraFollow.cs      bounded yaw/pitch chase, fixed pose + shared mouse/stick/arrow orbit sampling
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
      Interaction/   contracts, shops and bar/home/stairwell/supermarket/church doors
        CityTunnelTravel{Plan,Planner,Controller}.cs automatic unavailable crossing + visible return
        InventoryTargetInteraction.cs   reusable item requirement/menu state/handler contract
        PlayerAnimatedInteraction*.cs  positioning, static/moving pelvis targets + independent exit
        PlayerDoorAction{Controller,Target}.cs  guided door gesture, terminal commit + owned cleanup
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
      Scenes/        startup/loading plus seven gameplay roots, including ChurchInterior
        MainMenuRoot.cs                 black build-index-0 new-run boundary
        AreaLoadingRoot.cs              black unscaled progress-bar area transfer
        MountainRoadRoot.cs             standalone mountain world/player/UI composition
        MountainRoadWeather{Rules,Shaper}.cs  the city's own weather slot re-read by altitude, as snow and harder wind
        MountainRoadWindDriver.cs       carries that wind to the crowns, the cloth and the sound bed
        MountainRoadAtmosphere.cs       cold fog, time grade and flickering tunnel lamp
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
        CityMapAreaController.cs    tabs + stable two-area point catalog/XYZ selection
        CityMapAreaView.cs          tabs, mountain markers and travel presentation
        CityMapMountainRoadOverlay.cs full serpentine, ten apexes, bridge + terminal landmarks
        CityMapBusOverlay.cs        simplified blue loop + ordered localized stop markers
        CityMapController.cs        map input, city catalog, debug teleport + bar route
        CityMapView.cs              unified point hit/highlight/XYZ plus map presentation
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
      City/CityMiscAssetSetup.cs  FBX import/provider binding + strict manifest/root/bounds validation
      City/CityBuilding{AssetSetup,ModelImporter}.cs passive FBX import + four wrappers/provider
      City/Church{AssetSetup,ModelImporter}.cs Catholic FBX import, materials, prefabs + validation
      Bar/BarAssetSetup.cs       shared interior/exterior importer, prefab and manifest validation
      AudioMixerAssetSetup.cs  idempotent shared mixer topology and snapshot authoring
      MountainRoadCafeCastAssetSetup.cs  isolated model/clip import, validation + provider setup
      Player3D/       deterministic model/animation/portrait import + prefab setup
      City/NPC/       production/staged pedestrian Generic import, validation + prefab setup
      City/Traffic/   bus/driver FBX import, shared materials + Resources prefab setup
  Tests/
    Infrastructure/  shared run callback: mute listener output, then restore it
    EditMode/        layout plans, mixer DSP contract, sound synthesis and gameplay rules
      CityMiscAssetTests.cs       186-entry catalog/signature/provider + affected-builder smoke contract
      CityBuildingAssetTests.cs   4 prototypes / 24 meshes, importer, wrapper + provider contract
      CityBuildingPrototypeRuntimeTests.cs City/Home placement, collision, slot shader + half-space policy
      BarModelContractTests.cs    shared interior + complete bar_exterior_v2 manifest/runtime contract
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
      PlayerDoorActionPlanTests.cs explicit grounded dock/facing + independent poses
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
      CityMapMountainPresentationTests.cs west/south-only bounds + cave-mouth/tunnel map contract
      CityMountainBoundaryTests.cs       ridges, non-traversable cave + bent open tunnel invariants
      CityTunnelTravelCrossingTests.cs   inward-only crossing and retreat rearm
      CityTunnelLightingTests.cs         deterministic sparse flicker + bounded mono PCM
      CityTunnelShelterTests.cs          portal-depth/lateral shelter hysteresis
      MountainRoadTests.cs               route length/rise/hairpins/plateau/world contracts
      MountainRoadTerminalTests.cs       apron, landmarks, terrain blend + cabin clearance
      MountainRoadCafeCastTests.cs       roles/gaps/passive assets/clip blend/world ownership
      MountainCablewayTests.cs            loop continuity, world ownership and causal audio
      AreaTravelContractTests.cs         destination mapping and one-shot arrival state
      CityMapAreaPresentationTests.cs    tabs, player visibility + mountain schematic
      CityRiverPlannerTests.cs           core river builders + cave-aware south-rail ownership
      CityFringeYardTests.cs             five profiles, grounded portal/crown light, routes + light-free build
      CityFringeYardGroundWorldBuilderTests.cs exact terrain split, texture, UV + collider ownership
      CityFringeYardSurfaceAppearanceTests.cs measured sheets, imports + shared MPB application
      CityNightAtmosphereTests.cs         nearest fringe practical leases one existing street Spot
      HomeBalconyLayoutTests.cs         Home exterior layout/pedestrians + static stop pole
      SupermarketCityPlanningTests.cs     one eligible lot + open street approach
      CityOpenAreaDecorationPlannerTests.cs  yard identity, clearance and determinism
      CityCemeteryPlannerTests.cs         cemetery variety/clearance/determinism, textured build, night lamps + sittable benches
      CityMapViewportTests.cs             independent overflow axes, focus and clamping
      SupermarketInteriorLayoutTests.cs   room, paths, fixtures and finite slots
      SupermarketPurchaseRulesTests.cs    five offers, atomicity and new-run reset
      CatFeedingAnimationAssetTests.cs    cat sprite-track import and timing contract
      StairwellSurfaceAppearanceTests.cs  8 imports, shared MPBs and renderer coverage
      HomeSurfaceAppearanceTests.cs  12 imports, linear tint rule, dither `_BaseMap` guard and walk coverage
      StairwellCat{Interaction,Runtime,Asset}Tests.cs  branches, staging, pose/grin models and prefab contract
      ProjectBuildSceneTests.cs             startup scene order/allow-list
      HomeOpeningTimelineTests.cs           persistent 05:59 flicker and Wake-only 06:00
      GameTimeStateTests.cs                 freeze/start/elapsed delta/day/midnight/reset
      GameTimeDayNightRulesTests.cs         phase boundaries and smooth transitions
      GameWeatherRulesTests.cs              slot determinism, targets and boundary ramps
      CityWetSurfaceTests.cs                film timing/tint persistence + bounded grounded puddles
      CityDistrictPresentationPlannerTests.cs profiles, transitions + window schedules
      CityWindowAppearanceTests.cs          special families, stable district mix + night factor
      BarDistrictIdentityTests.cs           four identities and fallback contract
      BarSurfaceAppearanceTests.cs          worn sheets and district builder tints
      SupermarketInteriorAtmosphereTests.cs explicit baseline + local depth of field
      HomeAlarmClockPlanTests.cs            clock placement and circulation
      HomeRefrigerator{Plan,Timeline}Tests.cs  slots, approach and phase channels
      HomeBalconySmoking{Plan,Timeline}Tests.cs  dock, 3D clips, timing, drift + safe exit
      HomeRefrigeratorItem{Catalog,InspectionTimeline}Tests.cs  metadata and nested phases
      HomeOcclusion{Registry,Resolver}Tests.cs  group and ray contracts
      InteractionPromptViewTests.cs          prompt callback lifecycle
      InteriorSoundscapeSynthesisTests.cs   deterministic distinct loop contracts
      Audio/HomeAlarmClockSynthesisTests.cs generated ring contract
      Audio/CitySound*.cs                   causal plan/schedule/synthesis/occlusion contracts
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
      PlayerDoorActionPlayModeTests.cs terminal transition commit + cancellation cleanup
      Player3DGameplaySceneIntegrationPlayModeTests.cs  shared gameplay-root camera/hero contract
      Player3DVisualCapturePlayModeTests.cs  bounded scene framing capture
      BarDrinkFirstPersonArmsPlayModeTests.cs  prefab subsets + visibility restoration
ArtSource/
  Vehicles/
    Blender/                    generated bus .blend and deterministic preview
    Drivers/Blender/            generated driver .blend and deterministic preview
  Pedestrians/
    Blender/                    production/staged model sources, previews and animation contact sheets
  Player/
    PlayerDirectionalTurntable.png  retired 2D design source / visual lineage
    Blender/                    production .blend, transparent preview and authoring notes
    BedSleep/                    retired player-sprite source history
    BalconySmoking/              retired player-sprite source history
    CatFeeding/                  retired player-sprite source history
  Stairwell/
    Cat/Blender/                 generated 3D cat .blend + back-quarter and face previews
  City/
    mountain-contact-sheet.png    deterministic physical-ridge albedo review sheet
    mountain-textures.json        generated mountain texture manifest
    fringe-contact-sheet.png      forefield/service-track/concrete/masonry comparison sheet
    fringe-textures.json          measured fringe texture manifest
    Facades/                     facade albedo contract, contact sheet and the cell-grid README
    Blender/                     park chess, CityMisc3D and four-prototype CityBuildings3D sources/previews
  Bar/                           shared interior/exterior .blend, contact sheet and texture manifest
  Home/                          apartment albedo contract, manifest and contact sheet
  MountainRoad/                  mountain albedo contract, borrowed sheets + Blender misc source/preview
  Church/Blender/                Catholic `.blend` source + accepted exterior/interior previews
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
  build-stairwell-cat-3d-model.py   armature-free pivot-empty cat + grin-UV validator
  build-church-3d-model.py       deterministic Catholic exterior/interior Blender build + validator
  build-church-textures.py       deterministic Catholic surface/stained-glass/sacred-art sheets
  build-mountain-road-misc-3d-model.py  15 assemblies / 19 normalized roadside meshes
  build-city-misc-3d-model.py    64 kinds / 94 assemblies / 186 citywide role meshes
  build-city-buildings-3d-model.py  four fixed-metre district prototypes / 24 role meshes
  city_building_parts.py         pure deterministic building geometry + attachment/window metadata
  build-bar-3d-model.py          shared interior + complete fixed-metre pub exterior/export validator
  bar_exterior.py                deterministic 38-part late-Victorian pub geometry
  build-bar-textures.py          interior sheets + exterior brick/plaster albedos
  build-city-facade-textures.py     deterministic district wall albedos + validator
  build-city-poi-textures.py        deterministic district POI surface albedos + validator
  build-cemetery-textures.py        deterministic cemetery surface albedos (granite/stone/gravel/soil) + validator
  build-city-park-textures.py       deterministic park surface albedos (ground, objects and landmark materials) + validator
  build-city-mountain-textures.py   deterministic weathered-rock albedo + validator
  build-city-fringe-textures.py     four measured fringe albedos + validator
  build-mountain-road-textures.py   six measured mountain albedos + nine borrowed-sheet contracts + validator
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
City/MountainRoad map -> CityMapAreaController -> City / Mountain Road tabs
                                               -> current-area player marker only
                                               -> XYZ / C / gamepad Y inspection
                                                  -> deterministic per-area point catalog
                                                  -> click or Left/Right / D-pad selection
                                                     -> persistent highlight
                                                     -> area + localized name + world XYZ
                                                  -> no route/travel/teleport action
                                               -> other-area confirmation
                                                  -> AreaTravelService
                                                     -> AreaLoading (Single)
                                                        -> black progress bar
                                                        -> destination (Single)
MountainRoadRoot -> MountainRoadPlanner -> validated 620 m continuous climb
                                      -> 9 m exit tunnel, spawn at 6 m
                                      -> 4.8 m road / ten 6.4 m, R7.5 m hairpins
                                      -> <=8% grade / +26.1 m rise / final 5 m level
                                      -> lower five -> 50 m bridge -> upper five
                                         -> 5.8 m deck / >=25 m gorge
                                      -> joined ~42 x 27 m terminal
                                         -> visible colliderless R7.5 m apron on shared collision
                                         -> enterable five-sided glass cafe on left
                                         -> 58 m relative-height cableway on right
                                      -> route-wide forest/misc + middle/far snowy layers
                                         -> 76 m terrain + grounded perimeter ridges
                                         -> ridge footprints clear route and trees
                                      -> five positioned sounds incl. loose bridge rail
                  -> pure City plan for the City map tab only
blueprint ID + seed -> CityBlueprintCatalog -> immutable CityBlueprint
                                          -> stable area IDs + categories/profiles
                                          -> sparse active-cell topology
                                          -> north-south river corridor
                                             -> two road bridges + timber park bridge
                                             -> two promenades + four lower landings
                                          -> split 16-cell centered park
                                          -> north-edge beach + water
                                          -> default eastern Cemetery/yard areas
                                          -> default mountain boundary plan
                                             -> physical west/south ridges
                                                -> shared opaque physical shader
                                                -> 43-31 m dither handoff + fog floor
                                             -> low south river-cave mouth
                                                -> >48 m hidden water + bed throat
                                                -> two walkable promenades ending at rock
                                             -> open south-west tunnel stub
                                                -> 12 m physical / 72 m bent visual shell
                                                -> automatic refusal at 8 m, return to 6.5 m
                                                -> five lamps, one faulty positional ballast
                                             -> camera-relative west/south backdrop
                                          -> five typed fringe Yards
                                             -> west/south municipal service belt
                                                -> textured 22 m conforming forefield
                                                -> road/middle/toe bands, <=40 m anchor gaps
                                             -> open tunnel forecourt with >=6 m clear lane
                                             -> low eastern utility edge, no ridge
                                          -> CityLayoutGenerator -> validated CityLayout
                                           -> 13x12 envelope preserving 144 lots
                                           -> mandatory default outer Street circuit
                                              -> appended after seeded interior/access passes
                                              -> bank roads + two road bridges close the loop
                                           -> four UrbanBuilt areas + central park
                                           -> typed surfaces + open-area accesses
                                           -> one Residential home-frontage bar
                                              -> stable SplitTheG identity
                                           -> player home across its shared street
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
                                               -> core/cave promenades, bridges + lower landings
                                               -> first 11 m of the south tunnel
                                               -> complete BuildableGround regions
                                               -> radius-safe road/ground seams
                                               -> water/cave throat/unmapped/outside excluded
                                               -> physical colliders own obstacles
                                              -> PlayerMotor
                                          -> CityRoutePathfinder
                                             -> district-aware CityMap
                                                -> clipped readable viewport
                                                -> independent X/Y pan when overflowing
                                                -> mountain hatch/cave mouth/open tunnel arch
                                                -> display bounds grow west/south only
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
player + seed -> CityFogField (unchanged by time of day; cleared while tunnel-sheltered)
bus ride + tunnel shelter -> CityGameRoot combined shelter provider -> CityWeatherController
seed + session time -> GameWeatherRules -----------------------------> CityWeatherController
                                                                        -> CityRainField streaks (player-following dry core)
                                        -> CityRainSoundPlayer volume/cutoff
                                        -> CityWetSurfaceRegistry
                                           -> shared surface MPBs + slow drying
                                           -> CityPuddleWorldBuilder top-only batch
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
                            -> CityBusActor kinematic box + CityBusAudio
                               -> rear exterior diesel silent beyond the 48 m City view
                                  and rising only inside it
                               -> hero-only rear cabin/body layer
                               -> front/rear doorway pneumatics on real phase edges
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
seven gameplay roots -> PlayerFactory -> Resources/Player/Player3D.prefab
                                      -> 73 mesh bindings + 16 core parts
                                      -> 37 Generic in-place Actions
                                         -> Idle/Walk/face/status/fall
                                         -> 50-frame full-body Rise via all fours
                                         -> DoorUseEnter/DoorUseLoop/DoorUseExit
                                         -> BusBoardEnter/BusRideLoop/BusAlightExit
                                         -> ChessSeatEnter/ChessSeatPlayLoop/ChessSeatExit
                                      -> real URP mesh shadows
player -> PlayerContactShadow -> planted/fall-aware analytic patch
player -> PlayerInteractor -> InteractionPromptView -> same guarded Interact action
                         -> Route 01 front/rear door / fixed passenger seat
                            -> CityBusRideController board / later-stop exit
                         -> ordinary location door -> PlayerDoorActionPlan
                            -> guided dock + DoorUseEnter/Loop/Exit
                            -> terminal neutral pose -> SceneTransitionService
                         -> BarEntrance/BarExit -> BarInterior/City
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
                           -> StairwellCatActor -> 3D pivot articulation + grin
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
                    -> shared City appearance + complete collider-free pub exterior
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
                                 -> PlayerCharacterDimensions supine/seated support offsets
                                 -> HomeBedSurfaceDeformer (order 400) -> HomeBedSurfaceDepressionModel
                                    -> dents follow part penetration; sleeping hip descends by the sink
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
                                   -> BarDistrictIdentity
                                   -> BarInteriorWorldBuilder
                                   -> seven zones + four clear paths
                                   -> district palette/wall motif
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
           -> invariant gains: Music -5.5 / Beds -4 / Details +0.5 dB
                               World +2 / Gameplay +2.5 / UI +1.5 dB
scene transition -> preload -> outgoing theme fade-out -> activate destination
six present themes -> per-track EBU trim -> 12 kHz tone ----------> Music
City/Bar/Supermarket/Stairwell/Home -> scene fades ----------------^
Home smoking interaction -> smoking_theme + gain envelope --------^
scene root -> matching procedural ambience -----------------------> Ambience/Beds
City plans -> physical sound anchors -> CitySoundscapeDirector
           -> fixture loops/autonomous details -------------------> Ambience/Details
           -> carpet/scale owner events --------------------------> SFX/World
coast -> nearest finite waterline -> one spatial surf voice ------> Ambience/Details
lightning azimuth + distance -> delayed spatial thunder ----------> SFX/World
Home/Stairwell root -> spatial soundscape ------------------------> Ambience/Details
Home opening -> HomeAlarmClock -> spatial mechanical ring --------> SFX/Gameplay
input/gameplay events -> RetroAudioService -> pooled SFX/UI groups
music + compensated details/world sends -> reverb/echo -> Master compressor
URP post-processing -> 640x360 average -> subtle RGB555 blend -> point upscale
world composite -> crisp retro IMGUI overlay
```
