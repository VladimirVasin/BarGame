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
    AlpineVillage.unity
    MothersHouseInterior.unity
  Settings/
    CityNoirVolumeProfile.asset
    PCPresentationBaselineVolumeProfile.asset  project-owned Neutral/Bloom/Vignette baseline
    PC_RPAsset.asset              active PC pipeline + baseline volume reference
    PC_Renderer.asset             active PC PS1 renderer feature
  Environment/
    Clouds/
      Models/ExteriorCloudDome3D.{fbx,json}  generated unit hemisphere + deterministic manifest
      Textures/ExteriorCloudDensity.png      256 px linear broad/detail/erosion channels
      Materials/ExteriorCloud.mat            one shared instanced runtime material
  Resources/
    Environment/
      ExteriorCloudDome.prefab     passive 220-triangle cloud shell + asset metadata
    Materials/
      CityNoirEmission.mat
      HomeOccluderDither.mat       shared opaque Home foreground cutaway
      Ps1Composite.mat
      RuntimePrimitiveLit.mat      shared packaged URP/Lit runtime geometry
    Textures/
      CityBuildingSurfaces/       24 deterministic district/semantic sheets; facade/plinth Clamp, micro surfaces Repeat
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
      ExteriorCloud.shader        camera-relative density ceiling with manual haze horizon
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
      Player3D.prefab                   retained Hero V1 fallback prefab
      Player3DPortrait.png              retained V1 fallback portrait
      Player3DV2.prefab                 production adult-proportion modular hero
      Player3DV2Portrait.png            live inventory portrait from production V2
    Pedestrians/
      CityPedestrian3D.prefab           pooled Lampshade Walker presentation
      ChairCarrierPedestrian3D.prefab   pooled Chair Carrier presentation
      KettleHatPedestrian3D.prefab      pooled stout Kettle Hat Walker presentation + lid-pivot/spout-anchor rig metadata, detail atlas bound per renderer
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
    MothersHouse/
      MothersHouseInterior3D.prefab     passive 10 x 8 m two-storey shell, stair/rooms + typed anchors/parts; no gameplay components
      Textures/
        MothersHousePositiveAtlas.png   dedicated light, clean 4 x 4 room atlas; no Home/City sheets
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
    Cemetery/
      CemeteryRavenProvider.asset     serialized link to the passive 3D raven prefab
    City/
      YardWheelchairProvider.asset  serialized link to the staged yard rider prefab
      CityMiscAssetProvider.asset   259 passive City role-mesh bindings / 46,542 triangles + v4.9.0 signature
      CityBuildingAssetProvider.asset  four district prototype-prefab bindings + signature
      Buildings/
        OldTownPrototype01.prefab      passive fixed-metre wrapper + semantic registry
        ResidentialPrototype01.prefab passive fixed-metre wrapper + semantic registry
        IndustrialPrototype01.prefab  passive fixed-metre wrapper + semantic registry
        NightlifePrototype01.prefab   passive fixed-metre wrapper + semantic registry
    Bar/
      BarFacade3D.prefab            complete fixed-metre bar_exterior_v2 + door/sign anchors
      BarInterior3D.prefab          passive 178-mesh / 12,940-triangle bar_interior_v3 / generator 3.2.1 pub room
      BarServiceProps3D.prefab      passive 29-mesh bottle/vessel/menu/stream library / 1.2.0
      BarBartenderProvider.asset    active ordinary + retained legacy six-arm links
      Textures/                     fifteen 512 px interior albedos (five used by service) + two exterior sheets
    Supermarket/
      SupermarketInterior3D.prefab     passive 16 x 11 x 3.6 m authored shop + semantic registry
      SupermarketExterior3D.prefab     complete passive fixed-metre neighbourhood-store exterior
      Products/                        six imported per-item passive prefab outputs
      ExteriorTextures/               wall/fascia atlases + metric brick/metal albedos
      SupermarketCashierProvider.asset  active normal cashier prefab link
    PlayerHome/
      PlayerHomeExterior3D.prefab      passive Series 209-1-inspired home exterior + semantic registry
      ExteriorTextures/               nine dedicated stucco/brick/roof/wood/metal/frame/glass/concrete sheets
    MountainRoad/
      MountainRoadCafeCastProvider.asset  four isolated staged cafe prefab links
      Cafe/
        MountainRoadCafe3D.prefab         passive 61-mesh cafe, hinge-ready fridge/open hero menu + registry
        Textures/                         six 512 px semantic detail sheets
    Church/
      ChurchExterior3D.prefab          passive Catholic exterior + typed semantic anchors
      ChurchInterior3D.prefab          passive furnished interior + typed semantic anchors
  MothersHouse/
    Models/MothersHouseInterior3D.{fbx,json}  deterministic fixed-metre two-storey interior + contract manifest
    Localization/
      ru.json
      en.json
  Player3D/
    Models/
      PlayerCharacter3D.fbx             retained V1 fallback Generic model
      PlayerCharacter3D.json            retained V1 parts/bones/actions manifest
    Animations/
      PlayerCharacter3DAnimations.fbx   retained V1 37-action fallback set
    Materials/
      Player3DLit.mat                   shared URP/Lit hero material
    V2/
      Models/PlayerCharacter3DV2.{fbx,json}  production 34-part model + deterministic metrics
      Animations/PlayerCharacter3DV2Animations.fbx  production 38-action V2 rig, including Run
      Textures/PlayerFaceAtlas.png       4x4 five-expression point-filtered atlas
      Textures/PlayerClothingAtlas.png   full-colour open-jacket/trouser/boot atlas
      Materials/Player3DV2Clothing.mat  shared white-tint atlas material
  Pedestrians/
    Models/
      CityPedestrian3D.fbx              NpcHumanV2 street-walker model
      CityPedestrian3D.json             deterministic geometry/rig manifest
      ChairCarrierPedestrian3D.fbx      NpcHumanV2 chair-bearer model
      ChairCarrierPedestrian3D.json     deterministic geometry/rig manifest
      KettleHatPedestrian3D.fbx         NpcHumanV2 stout kettle-hat model (2,004 tris / 52 meshes, UV0 on 12 atlas parts)
      KettleHatPedestrian3D.json        deterministic geometry/rig manifest + signature_effects / rig_anchors / texture_bindings
      LongArmPedestrian3D.fbx           NpcHumanV2 narrow long-arm model
      LongArmPedestrian3D.json          deterministic geometry/rig manifest
      HelmetLampPedestrian3D.fbx        NpcHumanV2 hopping miner model
      HelmetLampPedestrian3D.json       deterministic geometry/rig manifest
    Animations/
      CityPedestrianLocomotion.fbx      shared 37-clip NpcHumanV2 locomotion/action library
      CityPedestrianLocomotion.json     gait/contact/clearance/apex + staged wheel-contact manifest
      MountainRoadCafeCast.{fbx,json}   isolated ten-clip sleep/interject/drink/wipe/walk/pour library + soundless tap/smoke idles
      NightlifeShelterResidents.{fbx,json} isolated three-loop warmer/seated/sleeper library
      MothersHouseMother.{fbx,json}     isolated one-clip bank: MotherRock, 6 s of breathing only — the rock belongs to the chair, not to her
    Textures/
      KettleHatDetailAtlas.png          256 px grey detail atlas (seams, chips, grooves, laces) multiplied by the kettle walker's palette tint
      MountainCafe*3DDetailAtlas.png    four role-specific 256 px face/clothing/hair/shoe atlases for the Hero V2-fidelity cafe cast
      NightlifeShelter*DetailAtlas.png  three 256 px garment/face atlases for the fixed arch residents
      MotherFaceAtlas.png               256 px 4x4 EXPRESSION grid (not a detail atlas): full colour, cell chosen at runtime by _BaseMap_ST, so its patch must be tinted WHITE
    Staged/
      Models/
        PipebackRoller3D.fbx            passive 31-bone wheelchair NPC model
        PipebackRoller3D.json           staged geometry/rig/passive-anchor manifest
        MountainCafe*3D.{fbx,json}      four distinct v2-detailed passive cafe role models/manifests
        NightlifeShelter*Resident3D.{fbx,json} three detailed passive Hero-Avatar resident models/manifests
        {YardBabushka,WeighbridgeAttendant,Cemetery*,LakeFisherman,Park*,LastRouteFerryman}3D.{fbx,json}
                                          eight further NpcHumanV2 staged roles/manifests
        Mother3D.{fbx,json}             the mother, seated forever; 45 meshes / 1892 tris (the hero is 34 / 1984), face_atlas block instead of texture_bindings, no detail atlas
      Prefabs/
        PipebackRoller3D.prefab         passive asset outside Resources and the runtime pool
        MountainCafe*3D.prefab          four cafe roles outside Resources/pedestrian pool
        NightlifeShelter*Resident3D.prefab three fixed textured shelter roles outside Resources/pedestrian pool
        Mother3D.prefab                 the mother, outside Resources/pool; her clip in the IDLE slot, walk slot deliberately empty, face atlas bound
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
      CityMisc3D.fbx                   122 citywide misc assemblies / 259 role meshes
      CityMisc3D.json                   roots, roles, bounds, compatibility + build signature
      CityBuildings3D.fbx               four fixed-metre district prototypes / 28 semantic meshes
      CityBuildings3D.json              surface/UV contract, envelopes, attachments, window slots + v2 signature
  Bar/
    Models/
      BarFacade3D.fbx                   38-part complete old-neighbourhood pub exterior
      BarFacade3D.json                  bar_exterior_v2 bounds, parts, door/sign anchors + signature
      BarInterior3D.fbx                 178-part / 12,940-triangle late-Victorian British-pub interior v3.2.1
      Bar3D.json                        v3.2.1 layout + 1.02 m counter/0.8175 m cafe stools/1.6175 m eye + signature
      BarServiceProps3D.{fbx,json}      29-part bottles/vessels/open menu/pour-stream pack / 1.2.0
    Bartender/
      Models/BarBartenderOrdinary3D.{fbx,json}  active two-arm NpcHumanV2 / v3.0.0 / 39 meshes
      Models/BarBartender3D.{fbx,json}          retained six-arm legacy model / v2.0.0
      Prefabs/BarBartenderOrdinary.prefab       provider-selected active bartender
      Prefabs/BarBartender.prefab               retained inactive six-arm asset
  Supermarket/
    Interior/
      Models/SupermarketInterior3D.{fbx,json}  passive authored hall, fixtures, pivots + measured contract
    Products/
      Models/SupermarketProducts3D.{fbx,json}  six-item passive product pack / 33 meshes / 2,276 tris
    Models/
      SupermarketExterior3D.fbx        complete passive 15.5 x 15.5 x 6.4 m exterior
      SupermarketExterior3D.json       semantic surfaces, bounds, door anchor + signature
    Cashier/
      Models/SupermarketCashier3D.{fbx,json}         active normal v1.0.0 / 40 meshes / 1,244 tris
      Models/SupermarketWatcherCashier3D.{fbx,json}  retained Bizarre v2.2.0 / 44 / 1,588
      Prefabs/SupermarketCashier.prefab               provider-bound active normal cashier
      Prefabs/SupermarketWatcherCashier.prefab        retained inactive Watcher asset
  PlayerHome/
    Models/
      PlayerHomeExterior3D.fbx         47-part passive 13 x 12 x 8.8 m home exterior
      PlayerHomeExterior3D.json        groups/surfaces/bounds/door + exactly-one-lit-window contract
  Church/
    Models/                             split Catholic exterior/interior FBX + shared manifest
    Textures/                           nine deterministic plaster/stone/wood/glass/art sheets
    Materials/                          shared URP bindings for the thirteen semantic slots
  Cemetery/
    Raven/
      Models/
        CemeteryRaven3D.fbx             armature-free pivot-empty cemetery raven
        CemeteryRaven3D.json            deterministic parts/pivots/wing-fold manifest
      Textures/
        CemeteryRavenDetailAtlas.png    256 px darkening feather/beak/scale detail atlas
      Prefabs/
        CemeteryRaven.prefab            passive asset outside Resources
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
        GameSessionState.cs       persistent clock/needs + debug day 1..7 + one-shot debug-map handoff
                                  -> SyncDayEvents/ApplyDayEvent: the one place a dated event changes the world
        GameDaySchedule.cs        pure event -> first-day table, looked up by id; FeedTheCatOpens = day 2
        GameTimeState.cs          frozen 05:59 -> running 06:00, elapsed delta + one-based day
        GameTimeRuntime.cs        persistent scaled-delta driver + day-announcement owner
        GameTimeDayNightRules.cs  night/dawn/day/dusk visual sample
        GameWeatherRules.cs       seeded 90-minute clear/light-rain/heavy-rain slots
        RuntimePrimitiveFactory.cs shared material primitives, oriented batches + opt-in XZ planar UVs
        NpcDesignAppearance.cs    exact 28-design Normal/Bizarre catalog (7/21; spawn-neutral)
        NpcSkinnedMeshCullingGuard.cs  live-pose bounds for every modular humanoid renderer
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
        CitySoundSchedulePlanner.cs        deterministic no-catch-up/rebase detail cursors
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
      Ambient/       scene-neutral outdoor raven roosts over the cemetery raven family
        RavenRoostPlan.cs  roost descriptors, per-scene settings and the ground perch-B ring resolver
        CityRavenRoostPlanner.cs  authored City candidates, 70 m spacing + hard exclusions
        MountainRoadRavenRoostPlanner.cs  planar-spaced road roosts + culvert fallback chain
        AlpineVillageRavenRoostPlanner.cs  village roosts clear of chapel, graves, house and station
        RavenRoostController.cs  one per scene: spawn-perched pairs, activation radius, shared clips
        (Audio) RavenCallClipCache.cs  static refcounted lease over the three shared caw clips
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
      World/         city plus validated bar/home/supermarket/mother-house plans and builders
        MothersHouseInterior{LayoutPlan,LayoutPlanner,LayoutValidator,WorldBuilder,WorldResult}.cs  ground + stair/corridor/two-room plan and collision
        MothersHouseMotherPlan.cs       where she sits, measured off the drawn cushion; one of her, no seed, no spawn band
        MothersHouseSofaSeatPlanner.cs  the sofa as one authored CityBenchSeat: south cushion, front-approach-only past the stair ramp
                                      pure room contract + imported-model composition
        MothersHouseInteriorAssetRegistry.cs  typed passive model anchors, parts and appearance
        MothersHouseKettleProp.cs     literal Kettle Hat prefab with only its ten kettle renderers visible
        MountainRoadCafe{AssetRegistry,ModelResources,SurfaceAppearance}.cs
                                      passive authored cafe presentation bridge
        MountainRoadCafeCollisionWorldBuilder.cs  exact 17-collider plan-owned shell
        MountainRoadCafe{ServiceTimeline,ServicePresentation,CupView}.cs role-staggered drink/fill/refill + repeatable seated menu handoff/post-exit retrieval
                                       hand/mouth-fitted cups, exact saucer return, counter-clear carry + per-frame spout-to-target stream
        MountainRoadCafeCigaretteEffect.cs woman idle phase -> separate ember glow + world-space SOCKET_Mouth exhale
                                       no separate clock, Light or AudioSource
        MountainRoadCafeConversation{Lines,Timeline,Controller,Look}.cs fixed ten-pair RU/EN bubble loop, cafe-volume gate + action-safe queue/head turns
        MountainRoadCafeSeatView.cs   cafe-stool first-person lifecycle + upright viewer-ray page focus/all-look lock/exact restore
        CounterSeat{Plan,Interaction,View}.cs reusable counter-seat camera/entry/exit ownership
        CounterMenu{Model,PageView,PropMotion}.cs shared open/rest/reopen/post-exit retrieval + opaque 0.40 s hinge fold/page/focus/grip motion
        BarServicePropFactory.cs     imported bottle/vessel/menu/stream role bridge
        MountainRoadCafeMenu{Model,Controller,Presentation}.cs three-item selection-only adapter over shared CounterMenu
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
        CityFringeYardLifePlanner.cs one grounded unoccupied mason cart; every other Yard vignette absent
        CityFringeYard{PracticalPlan,PracticalValidator}.cs four clear, deterministic practicals
        CityFringeYardSurfaceAppearance.cs four measured shared surface families
        CityFringeYardValidator.cs bands/gaps/all-safe-seams/corridors/vocabulary/budget invariants
        CityFringeYard{WorldBuilder,WorldResult}.cs imported utility/shed/gauge shells + Unity terrain, cables and practicals
        CityFringePracticalAnchor.cs runtime pose passed to the fixed night-light pool
        MountainRoadPlan.cs       typed 620 m route, ten hairpins, bridge, tunnel/plateau + dressing
        MountainRoadCompositionRules.cs pure road reveals + five-chapter natural-debris rhythm
        MountainRoad{Planner,Validator}.cs route/bridge + ridge envelope/footprint invariants
        MountainRoadBridge{WorldBuilder,Validator}.cs deck/beams/piers/open rails + bounded physics
        MountainRoadTerminal{Plan,Planner,Validator}.cs vehicle/cafe/cableway terminal contract
        MountainRoad{Terrain,Surface,Scenery}*.cs 76 m terrain, gorge, road + colliderless terminal apron
        MountainRoadSurfaceAppearance.cs six printed + nine borrowed measured surface families
        MountainRoadCafe{WorldBuilder,WorldResult}.cs imported enterable 61-mesh glass cafe composition
        MountainRoadCafeCast{Plan,Provider,AssetRegistry,Factory,Presentation,Controller}.cs ten-clip cast: every-third-exchange ignored husband one-shot + silent attendant/menu handoff + drinking, talking pair
        MountainCableway{Motion,Controller,WorldBuilder}.cs continuous cabins + causal machinery
        MountainCablewayDriveRules.cs   distance-driven brake/launch so a cabin docks ON the point
        AlpineVillage{Plan,Planner,Validator,TerrainSampler}.cs 82 m lane, OBB-safe plots + looming 74° / 60 m ridge, 12 m margin, brink mesh
        AlpineVillagePathPlan.cs visible path/traversal segments + shared dressing anchors
        AlpineVillageBrook{Plan,Planner,Builder}.cs 97 m spring brook: seeps, catch, swale cut into the sampler, ribbon water to the cableway cut
        AlpineSpringWaterResources.cs   still catch + running brook + road reach on the shared city water shader
        AlpineVillage{WalkableArea,WorldBuilder,WeatherShaper}.cs free bowl mask, two-submesh ground with one shared snapped edge, 2+1 house kit, warmth targets + permanent blizzard
        AlpineVillageRidgeAppearance.cs  village-only stable opaque 96-108 m haze handoff, 0.40 floor + floor-matched PS1 snap/world UV
        AlpineVillageStormField.cs       terrain-sampled ground spindrift + shared wind bed + gust-keyed storm wave rules
        AlpineVillagePeripheralStorm{Plan,Field}.cs route-distance side/rear curtains + protected full-house landmark aperture; presentation only
        AlpineVillageGarlandWind.cs      fixed-anchor wire deformation from shaped village wind
        Audio/AlpineVillageSoundscape*.cs eight deterministic causal spatial voices
        AlpineCableway{RidePlan,CabinSeat,RideController,RideFactory}.cs boarding, first-person ride, ridge fade
        VillageAssetProvider.cs         22 assemblies / 48 passive meshes: two ordinary closed-shell house archetypes, unique TopHouse + spring ledge/step/bed stones
        MountainRoadBrook{Plan,Builder}.cs water either side of the long-standing culvert, and the pour its sound anchor never had
        MountainRoadMiscAssetProvider.cs 19 passive Blender meshes + deterministic visual variants
        MountainRoadWalkableArea.cs route/plateau movement boundary
        MountainRoadWorldBuilder.cs separate mountain-only composition + 12 imported misc batches
        CityElevationStairPlacement.cs  sidewalk flight/landing integration
        CityExteriorStair{Plan,Planner,Validator}.cs guarded exterior flight contracts
        CityExteriorStairWorldBuilder.cs visible steps + one hidden ramp collider per flight
        CityRoadGroundBoundaryPlan.cs endpoint-sampled safe-connector/protected-drop classification
        CityTerrainSafetyWorldBuilder.cs segmented physical guards along dangerous sampled drops
        CityArchShelter{Plan,Placement,Planner,Validator}.cs fixed Nightlife gap, wall-attached terrace, steps, clear routes, props + rain volume
        CityArchShelterWorldBuilder.cs imported closed bridge/tableau, rigged residents + plan-owned collision/rain trigger
        CityArchShelterSurfaceAppearance.cs 15 exact-name measured-albedo MPB recipes
        CityArchShelterPresentation.cs layered flame, synced warm Point Light/sparks + crackle
        CityVerticalTraversalPlan.cs deterministic seam/frontage audit + spawn-road reachability
        CitySurfacePlan.cs       typed ground/water cells (incl. Yard OpenGround), datum and open-area access
        CityStreetIntersectionSelector.cs  shared stable zebra/signal node selection
        CityBusIntersectionSelector.cs safe Road v2.1 corner/three-/four-way apron selection
        CityStreetSurfacePlan.cs immutable oriented carriageway/sidewalk/marking geometry
        CityStreetSurfacePlanner.cs  graded strips, level pads, stair cuts, dashes + zebras
        CityWorldBuilder.cs      continuous terrain, fenced corner infill, river/bridges, graded streets, stairs + guarded drops
        CityBuildingPrototypePlacement.cs fixed-metre front/roof/facade poses + Home half-space classification
        CityBuildingPrototypeWorldBuilder.cs semantic building composition + inset foundation/collider authority split
        CityBuildingAssetRegistry.cs opening kind + one-to-one Residential balcony door/deck/dock metadata
        CityBuildingSurfaceAppearance.cs 24 district/opaque-surface recipes on one shared material through MPBs
        CitySpecialBuildingWorldBuilder.cs inset special-building foundations/collision + Home projection
        CityBuildingWindowSlotAppearance.cs UV2-addressed row-balanced warm/dark window binding
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
        CityCemeterySealedGraveWorldBuilder.cs  turned mound courses + one planner monument, slab omitted; publishes the mound-crown perch point
        CityChurchPlan.cs        `4 x 2` precinct, west door/approach, cemetery clearance + return
        CityChurchCourtyardPlan.cs  stone forecourt, north garden furniture/planting + reserved routes
        CityChurchCemeteryPassagePlan.cs  one 3 m middle-alley opening + safe shared threshold
        CityChurchCourtyardWorldBuilder.cs imported surface/fixture batches, collision + bench seats
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
        CityCourtyardPocket{Planner,Geometry}.cs up to four shallow Residential facade-side life pockets
        CityDecorationValidator.cs   landmark/core quotas, IDs and clearances
        CityMiscAssetProvider.cs     82 kinds / 122 assemblies / 259 passive role meshes with roots and bounds
        CityMiscAssetProvider.LateCatalog.cs exact courtyard/fringe late-wave part contracts
        CityBuildingAssetRegistry.cs fixed envelope, frontage, seven semantic roles, surface UV + window-slot contract
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
        CityWindowAppearance.cs      window sheet, shared warm lit materials + permanent fixture floor
        CityNightGlowRegistry.cs     registered electric glows (neon/signs/lamps) that die by day
        CityNightSiteLightRegistry.cs  authored site realtime lights scaled/disabled by the night factor
        CityFacadeGrid.cs            single source of the bay/floor pitch both walls and windows read
        CityFacadeAppearance.cs      district wall albedos tiled by that grid, not by metres
        CityBarFacadeWorldBuilder.cs complete fixed-metre pub exterior + preserved door/sign anchors
        BarExteriorSurfaceAppearance.cs dedicated brick/plaster/roof sheet binding
        CitySupermarketFacadeWorldBuilder.cs complete fixed-metre supermarket exterior + clipped Home fallback
        SupermarketExteriorAssetRegistry.cs semantic model parts/bounds/door-anchor bridge
        SupermarketExteriorModelResources.cs canonical Resources prefab lookup
        SupermarketExteriorSurfaceAppearance.cs wall/fascia/brick/metal/roof/glass/sign bindings
        CityPlayerHomeExteriorWorldBuilder.cs complete authored home placement + semantic material binding
        PlayerHomeExteriorAssetRegistry.cs parts, full/body bounds and exterior-door anchor bridge
        PlayerHomeExteriorModelResources.cs canonical Resources prefab lookup
        PlayerHomeExteriorSurfaceAppearance.cs nine dedicated sheets + isolated lit-glass variant
        SupermarketEntranceGeometry.cs  frontage, apron and fence-opening dimensions
        RoadFencePlan.cs         MapBoundary/DeadEnd/CornerGuard rails + clearance-opening metadata
        RoadFencePlanner.cs      unsupported edges, true Street terminals + default NE road-cap L
        CityNightFixturePlanner.cs  lamps/signals clear public ground and approaches
        CityNightWorldBuilder.cs imported street-lamp/signal shells + Unity bulbs, halos, controllers and Lights
        CityDayNightController.cs   session lighting + exterior night factor
        CityWeatherController.cs    per-frame weather sample -> rain, wet film, flash, thunder
        ExteriorCloud{Profile,MotionRules,Resources,Field}.cs  three profiles, absolute-time wind advection + camera shell
        ExteriorCloudCaptureCamera.cs  opt-in reflection/capture camera marker
        CityRainField.cs            seeded player-following streaks + local roof kill triggers
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
        HomeBalconyWorldBuilder.cs   window, open door, deck, safe rails, ashtray; no camera-crossing eave fascia
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
        SupermarketInteriorWorldBuilder.cs  authored shop placement + plan-owned collision and finite products
        SupermarketSecurityCameraWorldBuilder.cs  four authored corner CCTV pivots servoed at the hero
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
        CityPedestrianPresentation.cs  archetype Idle/Walk/Sit blend, grounding + seat alignment; raises Advanced(dt) after every graph write
        CityKettleHatRigAnchors.cs     lid pivot / spout anchor / head-local axes; asset metadata only
        KettleBoilModel.cs             pure seeded pressure-vent cycle: lid lift/tilt, steam rate
        CityKettleHatBoilEffect.cs     factory-attached always-on boil: pivot write on Advanced, code-built steam
        CityPedestrianAssetRegistry.cs prefab anchors, clips and MPB palettes
        CityWheelchairNpcAssetRegistry.cs passive future mechanism-pivot bindings; metadata only
        CityArchShelterResidentAssetRegistry.cs one textured Hero-Avatar model + quiet loop contract
        CityArchShelterResidentProvider.cs build-safe references to the three staged prefabs
        CityArchShelterResidentPresentation.cs independent manual PlayableGraph, no player input
      City/Balcony/  local passive smokers on authored Residential docks
        CityBalconySmokerPlan.cs        all-building candidate catalogue + bounded Home selection
        CityBalconySmokerDirector.cs    per-session local chance, activation and distance release
        CityBalconySmokerFactory.cs     roaming-prefab reuse + authored cigarette attachment
        CityBalconySmokerPresentation.cs hidden Hero SmokeLoop driver, 31-bone pose transfer, grounding + timed plume
        CityBalconySmokerRuntime.cs     scene-owned visibility and playable cleanup
      Yard/          staged yard roles plus bounded colliderless courtyard-life residents
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
        CityCourtyardResidentPlan.cs deterministic active residential-pocket stances, cap five; no fringe NPCs
        CityCourtyardResidentPresentation.cs borrowed generic idle/sit presentation, no new clips
        CityCourtyardResidentFactory.cs colliderless passive instances outside the roaming pool
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
        CemeteryRavenPlan.cs  the mound-crown perch and the vacant-ground perch, pure geometry
        CemeteryRavenPoseRules.cs  pure pose -> pivot-delta mapping for body, head, wings, tail
        CemeteryRavenIdleModel.cs  deterministic breathe/shift/ruffle/preen idle timeline
        CemeteryRavenHeadModel.cs  hysteresis head tracking with an 18 m fog cutoff
        CemeteryRavenFlightModel.cs  pure takeoff/return timelines: flare, touch-down, fold
        CemeteryRavenActor.cs  adopts pivots, perches, flies and hides one raven
        CemeteryRavenDirectorModel.cs  the pure pair state machine: arm, arrive, flush, return
        CityCemeteryRavenController.cs  polls the ledger's first sealed grave, drives both birds
        CemeteryRavenProvider.cs  the only serialized reference to the staged prefab
        CemeteryRavenRigAnchors.cs  pivot/renderer/atlas bindings; asset metadata only
        CemeteryRavenFactory.cs  instantiation + passive-presentation guard, warning degrade
        (Audio) CemeteryRavenCallSynthesis.cs  three synthesized caw variants on the village one-shot contract
        (Audio) CemeteryRavenVoice.cs  one bounded spatial voice per raven, seeded perched schedule
      Park/          the two old men's boards, once somebody sits down at one
        CityBoardGameController.cs  seat hookup, seated camera ownership, pointer/cursor input, opponent think clock + every board state as a spoken cue
      LastRoute/     the island's car, the one man waiting by it, and the journey out
        LastRouteCarPlan.cs        where the car stands, off the paving and clear of every way in - or anywhere at all, once it has driven
        LastRouteCarFactory.cs     the staged car, its doors, springs, halos and the passenger seat
        LastRouteCarDoors.cs       leaves that swing on their own hinges, and the swing-clearance rule both docks obey
        LastRouteCarSuspension{,Model}.cs  the body on springs, kicked by dismounts and seatings
        LastRouteCarSeatPlan.cs    the hero's dock, doorway waypoint and seated hip, all off drawn anchors
        LastRouteCarSeatViewPlan.cs  the seated eye, its look limits and the level-horizon rule
        LastRouteCarSeatInteraction.cs  the offer, the clip-driven passenger leaf and first-person camera ownership; from the seat, the one interactable also answers for whatever on the dash he is looking at
        LastRouteCarDashboard.cs   the dash driven: the glovebox lid on its hinge, the radio's two knobs and sliding needle, the lit dial and the speedometer needle, all on the runtime root's axes
        LastRouteCarDashboard{State,Target,Gaze}.cs  what he left changed (on the session, because the tunnel raises a new car), what he can look at, and the ray-against-drawn-bounds pick that decides which
        LastRouteCarRadioModel.cs  the tuning knob's eight detents and the speedometer's sweep - pure; what the radio plays is undecided and not here
        LastRouteCarGloveboxTimeline.cs  a lid that drops and is caught, and is pushed shut - two curves and their inverses
        LastRouteFerryman{Plan,Factory,Provider}.cs  the one authored man, read off the car that was actually placed
        LastRouteFerrymanPresentation.cs  five postures on one manual graph, and the metres the clips do not carry
        LastRouteFerrymanBoarding{Plan,Timeline}.cs  the drop, the walk round the nose and the door-open-sit-shut clock
        LastRouteFerryman{Coin,Coat,RigAnchors,Quips,Interaction}.cs  the toss, the hem, the sockets, the twelve lines and the one question
        LastRouteFerrymanAlightingTimeline.cs  the same three beats run backwards, to get him out at the far end
        LastRouteFerrymanRideStage.cs  the monotone ladder both areas build him from
        LastRouteCarDrive{Path,Model}.cs  one drivable centreline with the one place on it the car gives way, and how fast a car will take it - corners, the end of the road and a stop line all braked to the same way
        LastRouteCarDriver.cs      the engine on the runtime root: pose, steering, wheel roll and what the road does to the springs
        LastRouteCarCabinLight.cs  the light INSIDE the car: a plafond tilted BACK at the seats so the windscreen's inward pane and the bonnet stay at 0.000, a glovebox bulb that follows the lid's animated openness, and the instrument faces lit by emission because a vertical panel is the one thing no cabin lamp lights well; lens and dials burn always (§20), the realtime pool only while somebody is in the cabin
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
        PlayerMotor.cs             tank walk/back/run input + grounded guided walk approach
        PlayerPresentation.cs      3D motion/status/clip/visibility contracts
        PlayerFactory.cs           shared prefab spawn in all nine gameplay roots
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
        Player3DResources.cs            V2-default / explicit-V1 fallback instantiation
        Player3DCharacterPresentation.cs Idle/Walk/Run gait + physics handoff + full-body Rise sampling
        Player3DFaceAtlasPresenter.cs    merge-safe MPB face-cell texture selection
        Player3DRagdollController.cs     bounded 13-body failed-balance physics + pose recovery
        Player3DFirstPersonSubset.cs     prefab-derived camera-local arm filtering
        Player3DHeadVisibility.cs        the whole head off by bone rule, for a camera inside it
      Inventory/     pure item catalog, ordered session stacks and menu state
        InventoryTypes.cs           stable IDs, definitions and stack values
        InventoryState.cs           atomic bounded stack mutations + starters
        InventoryConsumableCatalog.cs food floors, relief and bottled servings
        InventoryMenuModel.cs       wrapping selection and examine state
        InventoryItemModelFactory.cs six authored product prefabs + low-poly models for other items
        HomeRefrigeratorInventoryAdapter.cs  slot sources -> inventory IDs
        SupermarketProductCatalog.cs five offers with localized metadata/prices
        SupermarketPurchaseRules.cs  pure finite-source/cash/stack validation
      Interaction/   reusable CounterSeat + shared CounterMenu input, shops and location doors
        CounterSeatInteraction.cs    physical authored approach/sit/loop/stand lifecycle
        CounterMenuInput.cs          shared W/S/D-pad selection and Space/West confirmation
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
        MothersHouse{Entrance,Exit}.cs existing village leaf -> room -> one-shot safe return
        SupermarketShelf{Station,ShopController,ShopView}.cs  physical shelf browser
      Scenes/        startup/loading plus nine gameplay roots, including AlpineVillage and MothersHouseInterior
        MainMenuRoot.cs                 black build-index-0 new-run boundary
        AreaLoadingRoot.cs              black unscaled progress-bar area transfer
        MountainRoadRoot.cs             standalone mountain world/player/UI composition
        AlpineVillageRoot.cs            standalone upper-village world/player/UI composition
        MothersHouseInteriorRoot.cs     two-storey world/player/UI, height-aware fixed shots, kettle, exit, sofa seat and the mother
        MothersHouseMother{Presentation,Factory,Provider}.cs  the seated mother: manual PlayableGraph, hips aligned to the drawn cushion VERTICALLY only, an open SetExpression nothing calls
        MothersHouseRockingChairMotion.cs  one angle turns the chair's two meshes AND her root; pivot derived from the runners' parabola, world poses driven, nothing reparented
        MothersHouseInteriorAtmosphere.cs  hearth + two windows + one floor practical, and one sourceless Hearth Floor Bounce leashed to 1.1 m so it can never be the banned ceiling fill
        MothersHouseInteriorSoundscape.cs  muffled wind + tick/tock + sparse timber settling
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
        HomeBalconyExteriorAtmosphere.cs  Balcony-only City fog/lights + pedestrian/smoker gate
        HomeOcclusionResolver.cs      five camera-to-player sample rays
        HomePlayerOcclusionController.cs  grouped dither fade/hold/restore
        HomeInteriorAtmosphere.cs     two practicals + bathroom/window Spots, grade and dust
        HomeBathroomLight*.cs         synchronized tube/halo/point/spill flicker
        StairwellFixedCameraController.cs  three height-selected fixed shots
        StairwellInteriorAtmosphere.cs flickering practicals, grade and dust
        SupermarketInteriorRoot.cs    layout/world/player/shop/UI composition
        SupermarketInteriorAtmosphere.cs  six shadowless practicals + flickering row
      BarInteriorRoot.cs            bar layout/world/patrons/drink-shop composition
      BarPatronWorldBuilder.cs      deterministic 6 booth + 2 counter + 3 table patrons with surface docks
      BarPatronDrinkingBehavior.cs  cafe sip + five surface rests, right-hand bind grips, support/head drift
      Bar/Bartender/                provider, registry, world builder, presentation + service choreography
      Drinks/        bar menu adapter, stable IDs, atomic purchases and physical service
        BarDrinkMenuPresentation.cs  nine priced rows + carried/open/resting booklet over shared CounterMenu
      Supermarket/Cashier/  normal observing cashier + retained inactive Watcher asset
        SupermarketCashierProvider.cs      one addressable ref to the off-Resources prefab
        SupermarketCashierAssetRegistry.cs bones, ordinary head/eye + renderer bindings
        SupermarketCashierFactory.cs       spawn on the plan anchor + passivity guard + magnet
        SupermarketCashierPresentation.cs  procedural hunch, bounded head/eye look + blink
        SupermarketCashierActor.cs         samples the hero, drives bounded attention + pose
        SupermarketCashierSurveillanceState.cs pure local-look/startle/blink-suppression logic
        SupermarketCashierBlinkState.cs    pure 6.5 s rare-blink cycle
        SupermarketCashierInteraction.cs   E talk stub on its own trigger
      Supermarket/Interior/ passive Blender hall bridge
        SupermarketInteriorAssetRegistry.cs  semantic parts/anchors, shared surfaces + measured metadata
        SupermarketInteriorModelResources.cs canonical Resources load/instantiate boundary
      Supermarket/Products/ passive six-item Blender product bridge
        SupermarketProductAssetRegistry.cs per-item parts/bounds/source metadata
        SupermarketProductModelResources.cs canonical per-item Resources load/instantiate boundary
      UI/            shared soot/bone 640x360 IMGUI, pause/inventory, HUD, maps and F9 debug
        RetroUiTheme.cs             flat frames, soot grain, semantic values + packaged Roboto/legacy fallback
        BalanceCheckView.cs         crisp overhead arc, arrow and risk meter
        CityMapAreaController.cs    tabs + stable two-area point catalog/XYZ selection
        CityMapAreaView.cs          tabs, mountain markers and travel presentation
        CityMapMountainRoadOverlay.cs full serpentine, ten apexes, bridge + terminal landmarks
        CityMapBusOverlay.cs        pale neutral loop + ordered localized stop markers
        CityMapController.cs        map input, city catalog, debug teleport + bar route
        CityMapView.cs              unified point hit/highlight/XYZ plus map presentation
        PauseMenuModel.cs           pure main/confirmation navigation and actions
        PauseMenuController.cs      shared-lock time/audio/input pause ownership + IMGUI
        InventoryController.cs      modal inventory, selection and atomic Eat/Drink input
        InventoryView.cs            640x360 status + DAY N/HH:MM/grid/description/command UI
        GameDayAnnouncementView.cs  queued Wake/midnight DAY N overlay outside modal locks
        InventoryIconLibrary.cs     point-filtered icons + dedicated 3D hero portrait
        InventoryItemPreviewRenderer.cs hidden live 3D RenderTexture stage
        InventoryTargetInteractionController.cs shared modal target menu + atomic consumption
        InteractionPromptView.cs    localized clickable contextual actions
        HomeRefrigeratorItemInspectionView.cs  hover label and PS1 item panel
        CounterMenuHintView.cs       shared compact W/S + Space world-menu hint/status
        MountainRoadCafeMenuHintView.cs cafe localization adapter for the shared hint
    Editor/          scene/build helpers and reproducible noir/PS1/audio asset setup
      MothersHouse/  fixed-metre FBX import, passive Resources prefab + manifest validation
      Environment/ExteriorCloud{AssetSetup,ModelImporter,TextureImporter}.cs  deterministic import/prefab validation
      City/CityMiscAssetSetup.cs  FBX import/provider binding + strict manifest/root/bounds validation
      Village/VillageAssetSetup.cs  village FBX import/binding; expectation derived from the runtime catalog
      City/CityBuilding{AssetSetup,ModelImporter}.cs passive v2 FBX import + four wrappers/provider
      City/CityBuildingSurfaceTextureImporter.cs path-specific Clamp/Repeat, max-size, mip and readability contract
      City/Church{AssetSetup,ModelImporter}.cs Catholic FBX import, materials, prefabs + validation
      Bar/BarAssetSetup.cs       v3 interior/exterior/service-pack import, prefab and manifest validation
      Bar/BarBartenderV2AssetSetup.cs ordinary bartender import/prefab/provider setup
      Supermarket/SupermarketExterior{AssetSetup,ModelImporter}.cs passive exterior import, Resources prefab + manifest validation
      Supermarket/SupermarketInteriorAssetSetup.cs passive hall import, anchor/part binding + manifest validation
      Supermarket/SupermarketProductAssetSetup.cs six extracted Resources prefabs + passive pack validation
      PlayerHome/PlayerHomeExterior{AssetSetup,ModelImporter}.cs passive import, prefab authoring + exact lit-window validation
      AudioMixerAssetSetup.cs  idempotent shared mixer topology and snapshot authoring
      MountainRoadCafeCastAssetSetup.cs  isolated v2 model/clip/256 px atlas import, validation + provider setup
      NpcHumanV2AssetSetup.cs       one batch rebuild/validation entry point for all 27 on-disk humanoid NPC designs; bartender/cashier swaps leave active cast unchanged
      City/NPC/CityArchShelterResidentAssetSetup.cs isolated three-model/atlas/loop prefab + provider pipeline
      MothersHouse/MothersHouseMotherAssetSetup.cs  her own pipeline: the shared descriptor reads clip names out of the ONE bank and demands a walk, and she has neither
      City/NPC/CityPedestrianTextureImporter.cs  routes pedestrian detail atlases to the Hero V2 atlas import contract (Point/Clamp/sRGB/256/no mip)
      Player3D/       production V2 atlas/import/prefab pipeline + retained V1 setup
      City/NPC/       production/staged NpcHumanV2 import, Hero V2 Avatar copy + prefab setup
      City/Traffic/   bus/driver FBX import, shared materials + Resources prefab setup
  Tests/
    Infrastructure/  shared run callback: mute listener output, then restore it
    EditMode/        layout plans, mixer DSP contract, sound synthesis and gameplay rules
      CityMiscAssetTests.cs       238-entry catalog/signature/provider + affected-builder smoke contract
      CityArchShelterTests.cs     fixed-gap geometry, 15 textured surfaces, resident integration, fire and rain contracts
      CityArchShelterResidentAssetTests.cs  Hero rig/atlas/loop and all-frame mattress-envelope contracts
      CityBuildingAssetTests.cs   4 prototypes / 28 semantic meshes, UV, importer, wrapper + provider contract
      CityBuildingSurfaceAppearanceTests.cs 24-sheet resource/import/shared-material + MPB contract
      CityBuildingPrototypeRuntimeTests.cs City/Home placement, six opaque bindings, inset foundation, slot shader + half-space policy
      BarModelContractTests.cs    pub v3/service-pack/exterior manifest and runtime contract
      SupermarketExteriorModelContractTests.cs dimensions, sheets, clearance, passive importer + prefab registry contract
      SupermarketInteriorModelContractTests.cs fixed metres, semantic/product anchors, sheets, passive prefab + layout parity
      SupermarketProductModelContractTests.cs six-item manifest/import/prefab/passivity/bounds contract
      PlayerHomeExteriorModelContractTests.cs dimensions, outward gallery, sheets, clearance + exactly one emissive pane
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
      Player3D/Player3DAssetImportTests.cs  retained V1 model/Actions/prefab contract
      Player3D/Player3DV2AssetPipelineTests.cs  production selection/prefab/atlas/rig/topology contract
      Player3DFacialAtlasTests.cs       face-cell selection + V1 fallback/bootstrap
      PlayerDoorActionPlanTests.cs explicit grounded dock/facing + independent poses
      CityStreetSurfacePlannerTests.cs  corridor split, zebra selection + dash exclusion
      CityTerrainSurfaceWorldBuilderTests.cs sampled mesh/collider/UV terrain contract
      CityVerticalTraversalAuditTests.cs     continuous seams + spawn-road reachability
      CityPedestrianPlannerTests.cs     deterministic radius-safe sidewalk routes
      CityPedestrianRuntimeTests.cs     production lifecycle + staged Pipeback isolation/bindings + kettle rig/atlas contract
      KettleBoilModelTests.cs           pure boil cycle: seeds, vent bands, dt hygiene, 2.75x parity
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
      MountainRoadCafeCastTests.cs       roles/gaps/v2 atlas density/clip blend/world ownership
      MountainRoadCafeConversationTests.cs fixed RU/EN pair loop, every-third completed-exchange husband interruption, wrap-safe queue + partner-only look
      MountainRoadCafeMenuModelTests.cs  viewer-ray/no-roll + delivery/open/rest/reopen/retrieval/close contracts
      CounterSeatPlanTests.cs       authored/fallback physical counter-seat geometry contracts
      MountainCablewayTests.cs            loop continuity, world ownership and causal audio
      MountainCablewayRideTests.cs        exact docking, boarding step, treads, return station
      AlpineVillageTests.cs               lane grade, OBB seed sweep, looming bowl, shared-edge two-submesh ground/brink, weather + teleport ground
      AlpineVillageStormVisibilityTests.cs breathing haze: base/peak/far plane, gust extraction guard, wave simulation, ridge floor
      AlpineVillagePeripheralStormTests.cs route mask, full-house aperture, rear closure + presentation-only field rules
      AlpineVillagePathTests.cs           visible-route coverage, full-agent corridor + frontage clusters
      Audio/AlpineVillageSoundscapeTests.cs causal owners, synthesis, schedules + warmth grade
      VillageAssetTests.cs                kit catalog, plan-owned collision, garland light budget
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
      CityChurchCourtyardPlanningTests.cs  10 m setback, linked court composition/determinism + sittable benches
      CityMapViewportTests.cs             independent overflow axes, focus and clamping
      SupermarketInteriorLayoutTests.cs   room, paths, fixtures and finite slots
      SupermarketPurchaseRulesTests.cs    five offers, atomicity and new-run reset
      CatFeedingAnimationAssetTests.cs    cat sprite-track import and timing contract
      StairwellSurfaceAppearanceTests.cs  8 imports, shared MPBs and renderer coverage
      HomeSurfaceAppearanceTests.cs  12 imports, linear tint rule, dither `_BaseMap` guard and walk coverage
      StairwellCat{Interaction,Runtime,Asset}Tests.cs  branches, staging, pose/grin models and prefab contract
      ProjectBuildSceneTests.cs             startup scene order/allow-list
      HomeOpeningTimelineTests.cs           persistent 05:59 flicker and Wake-only 06:00
      GameTimeStateTests.cs                 freeze/start/day setter/announcement/midnight/reset
      GameTimeDayNightRulesTests.cs         phase boundaries and smooth transitions
      GameWeatherRulesTests.cs              slot determinism, targets and boundary ramps
      ExteriorCloud{Asset,Field}Tests.cs    imported-art, profiles, motion, camera and visibility contracts
      CityWetSurfaceTests.cs                film timing/tint persistence + bounded grounded puddles
      CityDistrictPresentationPlannerTests.cs profiles, transitions + row-balanced window shares
      CityWindowAppearanceTests.cs          warm families, stable per-row selection + fixture factor
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
      RavenRoostPlanTests.cs               per-area determinism, spacing, counts, exclusions + perch Y sanity
      RavenRoostDirectorParameterTests.cs  pinned cemetery defaults + parameterized return/done timelines
      RavenRoostControllerTests.cs         0 Lights, 2 AudioSources per roost host, spawn-perched directors
      RavenCallClipCacheTests.cs           refcount honesty, edit-branch destruction and regeneration
      Audio/HomeAlarmClockSynthesisTests.cs generated ring contract
      Audio/CitySound*.cs                   causal plan/schedule/rewind/synthesis/occlusion contracts
    PlayMode/        audio routing/lifecycle, presentation, traversal and scene flow
      AutomaticTestAudioMutePlayModeTests.cs  silent listener-output contract
      PauseMenuPlayModeTests.cs            Escape, modal exclusion and exact restoration
      CityKettleHatBoilPlayModeTests.cs    lid rides the head in idle/walk/seated, steam on the spout, pool release, cabin clamp
      CityKettleHatVisualCapturePlayModeTests.cs  [Explicit] 3/6/12 m boil strips into Captures/KettleHat
      AlpineVillageStormVisibilityPlayModeTests.cs  600 running frames: far plane 110, fog == pure wave function, ridge density == fog, trough + crest reached; run alone
      ExteriorCloudIntegrationPlayModeTests.cs  City -> road -> village -> Home balcony ownership/gating + opt-in review frames
      RavenRoostPlayModeTests.cs           per-scene spawn-perched, activation radius and flush/return smoke
      InventoryPlayModeTests.cs            I/Escape, day/time/needs freeze and exact restoration
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
      NpcSkinnedMeshCullingPlayModeTests.cs  seven provider-prefab samples keep every skinned part on dynamic bounds
      IntoxicationStatusPlayModeTests.cs hybrid handoff, fixed root, one-phase Rise cleanup
      PlayerAnimatedInteraction3DPlayModeTests.cs   clip sampling, pelvis alignment and cleanup
      PlayerDoorActionPlayModeTests.cs terminal transition commit + cancellation cleanup
      MothersHouseInteriorPlayModeTests.cs room/kettle/light + real stair/two rooms + village-door round trip
      Player3DGameplaySceneIntegrationPlayModeTests.cs  shared gameplay-root camera/hero contract
      Player3DVisualCapturePlayModeTests.cs  bounded scene framing capture
      BarDrinkFirstPersonArmsPlayModeTests.cs  seated renderer suppression + live hidden vessel attachment
      BarDrinkPhysicalShopPlayModeTests.cs multi-seat menu rest/reopen/purchase/service/post-exit retrieval lifecycle
      MountainRoadCafePlayModeTests.cs  shipped-scene cup/saucer + hand/pot/counter contacts, silent phase idles and seat-camera restoration
      MountainRoadCafeMenuPlayModeTests.cs handoff, close/rest/reopen, stand restore + post-exit physical retrieval/no-effect contract
ArtSource/
  Environment/Clouds/Blender/    generated cloud-dome `.blend` and deterministic density preview
  Vehicles/
    Blender/                    generated bus .blend and deterministic preview
    Drivers/Blender/            generated driver .blend and deterministic preview
  Pedestrians/
    Blender/                    production/staged model sources, previews and animation contact sheets
  Player/
    PlayerDirectionalTurntable.png  retired 2D design source / visual lineage
    Blender/                    retained V1 .blend, preview and authoring notes
    BedSleep/                    retired player-sprite source history
    BalconySmoking/              retired player-sprite source history
    CatFeeding/                  retired player-sprite source history
  PlayerV2/
    Blender/                    generated production V2 .blend source
    Preview/                    production full/front/three-quarter/lower-body/expression PNGs
  Stairwell/
    Cat/Blender/                 generated 3D cat .blend + back-quarter and face previews
  City/
    mountain-contact-sheet.png    deterministic physical-ridge albedo review sheet
    mountain-textures.json        generated mountain texture manifest
    fringe-contact-sheet.png      forefield/service-track/concrete/masonry comparison sheet
    fringe-textures.json          measured fringe texture manifest
    Facades/                     facade albedo contract, contact sheet and the cell-grid README
    Blender/                     park chess, CityMisc3D and four-prototype CityBuildings3D sources/previews
    BuildingSurfaces/            24-sheet ordinary-building manifest + district/role contact sheet
  Bar/                           pub v3 interior/exterior/service `.blend`, 1024 px albedo sources + preview nodes
  Home/                          apartment albedo contract, manifest and contact sheet
  PlayerHome/                    generated exterior .blend/preview + nine-sheet manifest/contact sheet
  MountainRoad/                  mountain albedo contract, borrowed sheets + Blender misc source/preview
    Cafe/Blender/                generated fixed-metre cafe `.blend`
    Cafe/Preview/                deterministic Nighthawks-composition review PNG
  Supermarket/Interior/
    Blender/                     generated fixed-metre shop-interior `.blend`
    Preview/                     deterministic authored-interior review PNG
  Supermarket/Cashier/Blender/   active normal + retained inactive Watcher `.blend`/preview pairs
  Supermarket/Products/
    Blender/                     generated six-item product-pack `.blend`
    Preview/                     deterministic unbranded product review PNG
  Village/Blender/               village kit `.blend` source and contact sheet (no sheet of its own)
  Church/Blender/                Catholic `.blend` source + accepted exterior/interior previews
  MothersHouse/
    Blender/                     generated fixed-metre room `.blend`
    Preview/                     approved wide fixed-camera composition PNG
    Textures/MothersHousePositiveAtlas.prompt.md  4 x 4 clean/aged atlas source prompt + acceptance
tools/
  build-exterior-cloud-3d-model.py  deterministic hemisphere, packed density texture and export validator
  build-city-bus-3d-model.py         real-scale bus model/export validator
  build-city-bus-driver-3d-model.py  driver model/rig/export validator
  build-city-pedestrian-3d-model.py  compatible rig/model/export validator
  build-city-chess-set-3d-model.py   turned chessmen/draught meshes + height-ladder validator
  build-player-3d-model.py          retained V1 generator + shared action/bed validators
  build-player-3d-model-v2.py       production V2 anatomy/atlas/rig/export wrapper
  build-player-puppet-atlas.py      retired 2D player source tooling
  extract-player-bed-sleep-frames.py      retired player-sprite source tooling
  build-player-bed-sleep-atlas.py         retired player-sprite source tooling
  extract-player-balcony-smoking-frames.py retired player-sprite source tooling
  build-player-balcony-smoking-atlas.py   retired player-sprite source tooling
  build-player-cat-feeding-atlas.py       retired player-sprite source tooling
  build-stairwell-cat-3d-model.py   armature-free pivot-empty cat + grin-UV validator
  build-church-3d-model.py       deterministic Catholic exterior/interior Blender build + validator
  build-church-textures.py       deterministic Catholic surface/stained-glass/sacred-art sheets
  build-mothers-house-interior-3d-model.py  fixed-metre room, UV/triangle/anchor/export validator
  build-mountain-road-misc-3d-model.py  15 assemblies / 19 normalized roadside meshes
  build-mountain-road-cafe-3d-model.py  v1.2.1 / 61-mesh cafe, passive kitchen/menu + hinge/anchor/prop/collider/overlap validator
  build-village-3d-model.py      v3.0.0 / village_house_archetypes_v3, 17 assemblies / 43 outward-validated meshes; no doors/panes/new sheet
  build-city-misc-3d-model.py    82 kinds / 122 assemblies / 259 citywide role meshes
  build-city-buildings-3d-model.py  four fixed-metre district prototypes / 28 semantic meshes + UV/exact/near-layer validation
  build-city-building-surface-textures.py  24 deterministic district/semantic albedos + validator
  city_building_parts.py         pure deterministic building geometry + surface/UV/attachment/window metadata
  atlas_kit.py                   shared PNG canvas/writer + rect-based atlas and UV helpers (Hero V2 + pedestrians)
  city_building_coplanarity.py   pure exact + <3 cm broad visible-layer audit with synthetic controls
  build-bar-3d-model.py          v3.2.2 source: safe right stool/continuous service board/no single-seat sign + v1.2.0 service pack
  build-ordinary-bartender-3d-model.py active two-arm NpcHumanV2 bartender generator/validator
  bar_exterior.py                deterministic 38-part late-Victorian pub geometry
  build-bar-textures.py          fifteen measured interior/service albedos + exterior brick/plaster sheets
  build-supermarket-cashier-3d-model.py  normal/Watcher cashier build, export and contract validation
  supermarket_cashier_variants.py       shared variant descriptors + normal/head geometry helpers
  supermarket_cashier_detail_atlas.py   shared deterministic uniform-detail atlas schema and painter
  build-supermarket-interior-3d-model.py  60-mesh fixed-metre shop interior + anchors/passivity/export validator
  build-supermarket-products-3d-model.py  33-mesh six-item passive product pack + pivot/export validator
  build-supermarket-exterior-3d-model.py  36-part fixed-metre shop exterior + semantic/clearance/export validator
  build-supermarket-exterior-textures.py  wall/fascia atlases + repeatable brick/metal sheets and validator
  build-player-home-exterior-3d-model.py  47-part Series 209-1-inspired exterior + exact geometry/light validator
  build-player-home-exterior-textures.py  nine deterministic semantic exterior sheets and contact sheet
  build-city-facade-textures.py     legacy special-shell/Home-clipped 4x4 wall albedos + validator
  build-city-poi-textures.py        deterministic district POI surface albedos + validator
  build-cemetery-textures.py        deterministic cemetery surface albedos (granite/stone/gravel/soil) + validator
  build-cemetery-raven-3d-model.py  armature-free pivot-empty cemetery raven + wing-fold/atlas validator
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
                                  -> one-based day -> queued DAY N announcement
                                  -> PlayerNeedsProgressionState
                                     -> hunger 0..100 / 1440 game minutes
                                     -> fatigue 0..100 / 1080 game minutes
                                     -> integer inventory Status bars
                                  -> HomeAlarmClock HH:MM / Inventory DAY N + HH:MM
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
                                            -> imported 61-mesh shell / 7 stools / four-role tableau
                                            -> passive kitchen + undriven hinge-ready FridgeDoor
                                            -> Menu.Hero handoff -> 0.45 s locked upright viewer-ray focus at 0.50 m/FOV40
                                               -> W/S or D-pad -> Space/West no-op confirm or E close
                                               -> 0.40 s opaque hinge fold -> closed booklet: gaze to unfold / look away to stand
                                               -> completed exit -> WalkToMenu/TakeMenu/CarryMenuBack -> hidden at service dock
                                               -> later completed sit -> fresh handoff
                                            -> warm practical + cold stove task fixture + technical wash
                                         -> 230 m / 9-support / 8-cabin cableway on right
                                            -> boarding open; line brakes to a dock on request
                                            -> outboard platform: pedestal fills the track gap
                                            -> ride -> ridge fade -> AlpineVillage
                                      -> route-wide forest/misc + middle/far snowy layers
                                         -> 76 m terrain + grounded perimeter ridges
                                         -> ridge footprints clear route and trees
                                      -> five positioned sounds incl. loose bridge rail
                  -> pure City plan for the City map tab only
AlpineVillageRoot -> AlpineVillagePlanner -> validated village above the rope
                                        -> one 82.1 m lane, +6.4 m, 7.8% average
                                           -> no step over 0.18 m anywhere on it
                                        -> house at the head, highest thing in the village
                                        -> 12 houses either side, 4 authored variants
                                        -> chapel / adit / graves on side spurs
                                        -> ridge at 3.6 (74 deg), 60 m, toe 15 m out: the bowl looms
                                           -> steeper than the hero's own slope limit
                                           -> stable opaque haze handoff, same PS1 snap, exact shared floor/rise edge
                                        -> haze breathes on the raw gust: 0.017 between gusts, 0.045 at a crest
                                           -> one writer per frame, 110 m plane, house back at every trough
                                        -> return station: tension weight, no motor
                                        -> garlands: emissive bulbs, 5 real lamps
                                        -> permanent blizzard: snow .88-1, wind .82-1
                                           -> stretched upper flakes + terrain-low spindrift
                                           -> soft side/rear curtains outside every trodden route
                                              -> station-to-whole-house aperture stays clear
                                              -> no wall, damage, slowdown or fog ownership
                                           -> one bearing/gust rhythm + continuous wind bed
                                           -> canopy/cabin dry; uphill axis remains readable
                  -> pure City + mountain plans for the other two map tabs
                  -> existing top-house door -> MothersHouseInterior (Single)
MothersHouseInteriorRoot -> pure layout -> passive 10 x 8 m two-storey imported interior
                                         -> ground southeast shot: hearth + both windows + furniture
                                         -> north-entry stair rising south -> west upper corridor
                                         -> two separate empty rooms -> height-aware fixed shots
                                         -> hidden ramp + split slabs/partitions/guards as runtime collision
                                         -> centred south entrance -> north-facing player spawn
                                         -> floor lamp; no invisible ceiling fill
                                         -> calm fire/wind/tick-tock/timber ASMR bed
                                         -> dedicated positive atlas; no Home/City environment sheets
                                         -> exact Kettle Hat prefab instance on the tea table
                                         -> exit -> one-shot safe AlpineVillage return outside the trigger
blueprint ID + seed -> CityBlueprintCatalog -> immutable CityBlueprint
                                          -> stable area IDs + categories/profiles
                                          -> sparse active-cell topology
                                          -> north-south river corridor
                                             -> two road bridges + timber park bridge
                                             -> two promenades + four lower landings
                                          -> split 16-cell centered park
                                          -> north-edge beach + water
                                          -> default eastern Cemetery/Church/yard areas
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
                            -> simplified pale neutral closed route
                            -> five default numbered localized hover stops + compact legend
                            -> below darker bone-toned player route; no live bus marker
nine gameplay roots -> PlayerFactory -> Resources/Player/Player3DV2.prefab
                                      -> 34 mesh bindings + 16 core parts
                                      -> 38 Generic in-place Actions
                                         -> Idle/Walk/Run/atlas-face/status/fall
                                         -> 50-frame full-body Rise via all fours
                                         -> DoorUseEnter/DoorUseLoop/DoorUseExit
                                         -> BusBoardEnter/BusRideLoop/BusAlightExit
                                         -> ChessSeatEnter/ChessSeatPlayLoop/ChessSeatExit
                                      -> real URP mesh shadows
explicit fallback -> Player3DVariant.ProductionV1
                  -> Resources/Player/Player3D.prefab + V1 portrait + frozen 37 Actions
player input -> W / left stick forward -> 2.6 m/s walk
             -> either Shift or L3 held + positive forward -> 4.2 m/s run
             -> S / left stick backward -> 1.4 m/s backpedal
             -> intoxication multiplier -> actual constrained motion -> Walk/Run blend
             -> scripted interaction approach -> walk speed only
player -> PlayerContactShadow -> planted/fall-aware analytic patch
player -> PlayerInteractor -> InteractionPromptView -> same guarded Interact action
                         -> Route 01 front/rear door / fixed passenger seat
                            -> CityBusRideController board / later-stop exit
                         -> ordinary location door -> PlayerDoorActionPlan
                            -> guided dock + DoorUseEnter/Loop/Exit
                            -> supermarket City entrance only
                               -> 0.242 m road/curb prompt-height tolerance
                               -> same physical guided dock
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
                                             -> shared product prefabs: vodka / egg / open stew can
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
       -> BarPatronWorldBuilder -> deterministic 11-person furniture/surface-bound cast
                                -> 6 booth seats at 0.48 m + 2 cafe stools at 0.8175 m
                                -> 3 standing table leans + bottles; no Babushka/Chess/Checkers
       -> BarSoundscape -> spatial crowd bed + rare bar cues
       -> BarArrivalPresentation -> skippable Bezier camera reveal
       -> four BarCounterStations -> CounterSeatPlan/View/Interaction
                            -> per-free-stool safe approach/exit -> physical sit -> 1.6175 m eye
                            -> rightmost x4.00 clears counter return; no floor/sign selector
                            -> shared CounterMenu model/input/page/hint/prop motion
                               -> bar adapter: 5 + 4 localized priced rows at 1.10 m / FOV 60
                               -> failed purchase stays open
                               -> E or success drives 0.40 s opaque hinge fold/rest
                               -> closed gaze unfolds / look-away stands
                            -> BarDrinkShop
                               -> retail catalog + atomic cash/drink transaction
                               -> BarDrinkServicePlan -> nine physical bottle slots
                               -> BarDrinkServiceWorldBuilder
                                  -> Blender service pack: 9 bottles + 5 vessels + stream + menu
                               -> BarDrinkServiceTimeline
                                  -> bartender pickup -> pour -> 3 s drink on hidden vessel attachment -> return
                                  -> seated world body stays visible; both camera-local arm meshes stay disabled
                                  -> closed menu remains through service -> completed stand/restore -> bartender retrieval
                                  -> later sit -> fresh delivery to the selected station
                               -> GameSessionState wallet + drinking progress
       -> SupermarketInteriorLayoutPlanner -> validated 16x11x3.6 shop
                                             -> three physical shelf views
                                                -> exact product-pack anchors
                                                   -> noodles + day-old loaf
                                                   -> vodka + closed stew
                                                   -> chicken egg
                                             -> decorative checkout + normal cashier
                                                -> ordinary fixed-length neck
                                                -> bounded eye/head tracking from the till
                                                -> retained Watcher asset is never instantiated
                                                -> E talk stub -> placeholder line
                                             -> four corner CCTV heads track the hero
                                             -> six practicals + one flickering row
       -> SupermarketShelfStation -> product-centered authored shelf camera
                                   -> renderer-only hero + cashier hide leases
                                      -> exact state restore on every exit
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
                          -> direct day 1..7 -> day index only, keep HH:MM/needs
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
world composite -> crisp soot/bone IMGUI overlay
```
