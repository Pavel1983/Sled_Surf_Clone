# TechDesign — project architecture

A runner prototype in the style of Sled Surfers: a slingshot launch, a ride down a snowy road, steering, obstacles, coins, and upgrades between runs.

Unity `6000.5.11f1`, URP, the Input System and the Splines package. One scene: `Assets/_Game/Scenes/SampleScene.unity`.

The rules the code follows are in `CodeRules.md`.

---

## 1. Layers

```
[ UI ]            [ Game ]                       [ Core ]          [ Data ]
 RideHud           SlingshotLaunch  SplineRoad    RunEconomy        PlayerProgress
 SteerStick        SledRider        PathCoins     SlingshotAim      ProgressRepository
 UiArc             SlideFacing      Obstacle      UpgradeId
 UiRoundBar        BallFollowCamera RunAudio
                          ↑
                   GameBootstrap — composition root
```

| Layer | Folder | Contents | Depends on |
|-------|--------|----------|------------|
| **Core** | `Scripts/Core` | Coin and upgrade rules, the slingshot aim maths. Plain C# classes. | Nothing in the scene |
| **Data** | `Scripts/Data` | The saved progress and a repository on `PlayerPrefs`. | Nothing |
| **Game** | `Scripts/Game` | Objects of the scene: road, sled, rider, coins, obstacles, camera, sound, composition root. | Core |
| **UI** | `Scripts/UI` | The HUD and the steer stick. | Game, Core |
| **Editor** | `Scripts/Editor` | Level tools. | Game |

Core and Data know nothing about `MonoBehaviour`, Canvas or the scene. The UI does not compute rules and does not write the save: it shows ready values and calls methods.

---

## 2. Composition root

`GameBootstrap` is the only place that creates objects outside the scene and connects events. It runs before the other scripts (`DefaultExecutionOrder(-100)`), once, in `Awake`:

1. Creates `ProgressRepository`, loads `PlayerProgress`, creates `RunEconomy`.
2. Hands the economy to the objects that read it: `SlingshotLaunch.Initialize`, `RideHud.Initialize`.
3. Subscribes the events (the table in §4).
4. Collects every `Obstacle` under its obstacle roots (`Obstacles` and `Decor`) and subscribes it to the restart.

How dependencies are wired:

- **A scene object or an asset** is a `[SerializeField]` field set in the inspector. Game code has no `Find*`, no `Camera.main` and no `Resources.Load`.
- **A plain C# object** (`RunEconomy`) arrives through `Initialize` from `GameBootstrap`.
- **Events** are subscribed by `GameBootstrap`.

---

## 3. Scene objects

| Object | Components | Role |
|--------|------------|------|
| `Game` | `GameBootstrap` | Composition root. |
| `Level1_Road` (prefab) | `SplineRoad`, `SplineContainer`, `MeshCollider` | The road: geometry, relief, snow, friction. |
| `Slider` | `Rigidbody`, `CapsuleCollider`, `SlingshotLaunch`, `SledRider` | The sled: physics, aiming, run state, the rider. |
| `Slider/Visual` | `SlideFacing` | The point the model hangs on. It is planted on the snow and tilted with the slope. |
| `Slider/SnowSpray`, `PullLine`, `ShotLine` | `ParticleSystem`, `LineRenderer` | Snow spray and the aim lines. Code only turns them on and sets their points. |
| `Main Camera` | `BallFollowCamera` | The camera behind the sled. |
| `Path Coins` | `PathCoins` | Coins along the road. |
| `Obstacles` | child `CrashObstacle` / `SlowObstacle` objects | The obstacles of the level. |
| `Decor` | `DecorVisibility`, child `CrashObstacle` / `SlowObstacle` prefab instances | Street props along both sides of the run. Each one is an obstacle with a box collider. `DecorVisibility` switches on only the ones within range of the sled. |
| `HUD` | `Canvas`, `RideHud` | The whole interface. |
| `Steer Stick` | `Canvas`, `SteerStick` | The floating stick. |
| `Run Audio` | `AudioSource`, `RunAudio` | Every sound of a run. |
| `EventSystem` | | Input for the UI. |

The HUD hierarchy, the stick, the particles and the lines live in the scene and are not created by code. Code creates only what depends on data: the coins along the road, the pieces of generated road geometry, and the copy of the rider model.

---

## 4. Events

| Event | Raised by | Listener |
|-------|-----------|----------|
| `SlingshotLaunch.Armed` | TAP TO PLAY is pressed | `RunAudio.PlaySitDown` |
| `SlingshotLaunch.Slowed` | a slowing obstacle | `RunAudio.PlaySlowHit` |
| `SlingshotLaunch.Crashed` | an obstacle that ends the run | `SledRider.PlayCrash`, `RunAudio.PlayCrash` |
| `SlingshotLaunch.RunFinished` | the run ends for any reason | `GameBootstrap` → `RunEconomy.CommitRun` |
| `SlingshotLaunch.RunReset` | the run returns to the start | `SledRider.StopCrash`, every `Obstacle.Restore` |
| `PathCoins.Picked` | a coin is picked up | `RunAudio.PlayCoin` |
| `RideHud.Clicked` | any HUD button | `RunAudio.PlayClick` |
| `RunEconomy.Changed` | coins or levels changed | `GameBootstrap` → `ProgressRepository.Save` |

The rest of the communication is polling through a direct reference: the HUD, the stick, the camera and the coins read the properties of `SlingshotLaunch` every frame.

---

## 5. Run lifecycle

`SlingshotLaunch` holds the state of the run and exposes it through properties.

```
start ──TAP TO PLAY──▶ aim ──release──▶ ride ──▶ stop ──CONTINUE──▶ start
PlayArmed=false        PlayArmed=true   IsRiding  IsStopped
                       IsAiming on pull CanSteer  ResultsReady
```

| Phase | What happens |
|-------|--------------|
| **Start** | The sled sits at the start of the road, the body is kinematic. The HUD shows the upgrade shop. The rider stands facing the camera. |
| **Aim** | The finger pulls back from the sled. Pull length is speed, the angle is up to ±20° from the road axis. `SlingshotAim` snaps the angle to steps and computes the power lost at the edges. |
| **Ride** | The body is dynamic. Every physics step: check for the end of the run, steer, align the capsule, apply friction and clear the snow trail. |
| **Stop** | The body is kinematic again and `RunFinished` is raised. After a crash the results card waits `crashResultsDelay` seconds while the fall plays. |

A run ends when the sled hits a `CrashObstacle`, reaches the finish (`finishFraction` of the spline length), stays in place for `restDelay` seconds, or starts sliding backward.

A restart increases `RunSerial` and raises `RunReset`. The coins watch `RunSerial` to know when to come back.

---

## 6. The road — `SplineRoad`

The largest class of the project. It builds the road from a spline and answers questions about its surface.

**Surface coordinates.** Any point of the road is a pair `(t, u)`: `t` runs from 0 to 1 along the spline, `u` from 0 (left edge) to 1 (right edge). All road data is stored in these coordinates.

**What it stores:**

- the width and the cross-section profile (a bowl or a crown, with keys along the road);
- the relief grid `sculptHeights`: 160 × 32 heights painted by the Road Sculpt tool;
- the snow mask: a byte per cell, where 255 is snow and 0 is cleared ice.

**What it builds:**

- a coarse mesh for the `MeshCollider`, which the sled rides on;
- a denser visible mesh cut into chunks, so the camera draws only the near ones;
- invisible side walls.

**Queries the rest of the code uses:**

| Method | Purpose |
|--------|---------|
| `TryGetSurfaceCoord(world, hintT, …)` | World point → `(t, u)`. With a `hintT` it searches a 48 m window around the last position, without one it searches the whole spline. |
| `TryGetSurface(t, u, …)` | `(t, u)` → the point on the surface, the tangent and the spline's up vector. |
| `TryRaycast` | A ray against the road collider that also recovers `(t, u)`. Used by the editor tools. |
| `FrictionAt`, `SnowCoverage` | Friction and snow coverage at a point. |
| `ClearSnowAlong` | Clears the snow along a segment: the sled's trail. |
| `ApplyContactFriction` | Writes the friction into the road's physics material. |

**Friction.** The sled has zero friction, and the road has whatever the sled wrote on this step. Snow slows more than ice, so riding in your own trail is faster. The `Friction` upgrade scales that value.

**Snow on screen.** The mask is uploaded to a texture. The `RoadSnow` shader blends snow with ice by it and raises the vertices. A ride uploads only the rectangle that changed.

---

## 7. The sled — `SlingshotLaunch`

**Body.** A `Rigidbody` with frozen rotation and a `CapsuleCollider` lying along the Z axis, because the rider slides lying down. The capsule and the body are configured in the scene.

**Alignment (`AlignBody`).** The solver never rotates the body. Every physics step the code turns its nose along the velocity and lays it on the slope: the normal comes from a ray against the road collider and is smoothed. In the air the tilt is kept.

**Steering (`ApplySteer`).** The stick sets a target from −1 to 1. It goes through a response curve and smoothing, and the result is a share of the sideways acceleration the sled can take (`groundLateralG`, in g). On a curve that acceleration is speed times turn rate, so the turn rate is `a / v`: sharp at low speed, gentle at high speed. `maxTurnRate` caps it where the sled is slow. The velocity is rotated by that rate and its magnitude does not change. The acceleration limit is lower in the air.

**Friction setting.** `Improved Patch Friction` is on in `Project Settings → Physics`. Without it a capsule with several contact points is slowed more than it should be.

**Obstacles.** `OnCollisionEnter` finds the `Obstacle` of the touched collider and calls `Hit`. `CrashObstacle` calls `Crash()`, `SlowObstacle` calls `SlowDown(keepSpeed)`. The speed after a slow-down is taken from the speed before the impact, because by the time of the callback the impact has already bent it.

**Snow spray.** Particles are emitted by hand at the contact point. Their number, spread and size grow with speed.

---

## 8. The rider — `SledRider` and `SlideFacing`

`SlideFacing` moves the `Visual` object: it plants it on the road surface under the sled and tilts it with the slope. In the air the model eases back upright and flies with the body. The position comes from the interpolated transform, otherwise the model stutters against the camera.

`SledRider` places a copy of the model under `Visual` and drives its poses through a `PlayableGraph` with a four-input mixer:

| Input | Clip | When |
|-------|------|------|
| Idle | `Ilde_Breathing` | The start screen. Looped by hand. |
| Sit | `Run_to_Slide` | After TAP TO PLAY. Scrubbed by hand, so the pose and the hip pin move together. |
| Slide | `Slide` | The ride. |
| Crash | `Death2` | After a crash. Stops on its last frame. |

One number, `seat`, from 0 (standing) to 1 (seated), drives three things at once: the clip weights, the turn from the camera to the road, and the pin of the hips to the point on the snow. While she is seated the hips are pulled back to the origin of `Visual` every frame, otherwise the slide clip rocks her against the sled.

---

## 9. The camera — `BallFollowCamera`

The camera chases a point behind the sled with a PID controller: P and I pull toward the point, D damps the camera's own speed. The lag grows with the sled's speed, so it is capped: up to half of `maxLag` the camera moves freely, beyond that it eases into the limit.

---

## 10. Coins and obstacles

**`PathCoins`** places the coins in code: the gap along the road is random within set bounds, and the lanes alternate. A coin is two discs of a generated mesh with materials from assets. Only the coins near the sled are active, because the road is several kilometres long. A pickup is a distance check against the sled.

**`Obstacle`** is the base class of an obstacle placed by hand. It fires once per run: it turns its colliders off, hides its model if asked to, and calls `Apply`. A new obstacle type is a subclass with one method.

---

## 11. Economy and saving

| Class | Layer | Responsibility |
|-------|-------|----------------|
| `PlayerProgress` | Data | The saved fields: coins, three levels, the number of runs. |
| `ProgressRepository` | Data | `Load` and `Save` through `PlayerPrefs`. It knows no rules. |
| `RunEconomy` | Core | The rules: what a run pays, what an upgrade costs and what it changes. |

A run pays for distance (`IncomePerKilometer`) plus the coins picked up. It opens at the launch (`BeginRun`) and closes in `CommitRun`. A second `CommitRun` for the same run adds nothing, and upgrades cannot be bought while a run is open.

`RunEconomy` does not decide when to save: it raises `Changed`, and `GameBootstrap` calls `ProgressRepository.Save`.

---

## 12. Interface

**`RideHud`** shows four screens of one Canvas: the tension meter while aiming, the ride HUD, the start screen with the shop, and the results card. It picks the visible screen from the properties of `SlingshotLaunch`. At runtime it changes only what depends on data: the texts, the fill of the gauges, the height of the progress bar, and the color of an upgrade button by whether it can be bought.

**`SteerStick`** shows a ring where the finger pressed and writes the deflection into `SlingshotLaunch.SteerInput`. A press on a UI element does not count as steering.

**`UiArc`, `UiRoundBar`** are custom `Graphic` classes for arcs and capsules: they draw a mesh with a feathered edge and no textures.

---

## 13. Sound

One `AudioSource` on the `Run Audio` object. The clips are listed in the `RunSounds` asset. Game code never plays a sound: it raises events, and `GameBootstrap` connects them to the methods of `RunAudio`.

Two sounds have no event, so `RunAudio` watches the sled for them itself: a landing (in the air for more than 0.3 s) and a sharp turn (the stick crossed from one side to the other in less than 0.45 s).

---

## 14. Editor tools

| Tool | Where | What it does |
|------|-------|--------------|
| Road Sculpt | the Scene view toolbar, with the road selected | Raises and lowers the relief with a brush. |
| Road Snow | the same place | Clears or restores the snow. |
| Snap Selection To Road | `Tools → Road` | Seats the selected objects on the surface and tilts them to the normal. |

---

## 15. Assets

| Folder | Contents |
|--------|----------|
| `Art/Ladybug` | The rider's model, clips and texture. |
| `Art/Obstacles`, `Art/Road` | The obstacle meshes and their texture, the road texture. |
| `Art/UI`, `Art/Fx` | The HUD and stick sprites, the snowflake texture. |
| `Audio` | The clips and the `RunSounds` asset. |
| `Materials`, `Shaders` | The materials and the snow road shader. |
| `Prefabs` | The road and the obstacles. |
| `Settings` | The URP assets and the Input System actions. |

---

## 16. Known limitations

- **`SplineRoad` and `SlingshotLaunch` are too large.** The first holds geometry, relief, snow and mesh building in one class. The second holds aim input, physics, steering, run state and the spray. They should be split, but that was left alone so the behaviour would not change before the hand-in.
- **The short names `t` and `u`** are kept as the common notation for surface coordinates.
- **The HUD font** is the built-in `LegacyRuntime`. The HUD does not use textures or a font from the Ladybug pack.
- **A device build was not tested.** Some materials of the generated road geometry are created through `Shader.Find`, and such a shader may be missing from a build.
