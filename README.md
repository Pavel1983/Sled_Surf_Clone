# Sled Surf prototype

A playable downhill run in the style of Sled Surfers, built with the Miraculous Ladybug asset pack.

## Unity version

Unity `6000.5.11f1` with the Universal Render Pipeline and the Input System.

## How to run

1. Open the project in that Unity version.
2. Open `Assets/_Game/Scenes/SampleScene.unity`.
3. Press Play.
4. On the start screen, buy upgrades if you have coins, then press **TAP TO PLAY**.
5. Drag back from the sled to pull the slingshot, then release. A longer pull launches faster. The shot stays within 20 degrees of the road.
6. During the run, press and drag anywhere on the screen. The stick appears under the finger and steers left and right. The sled turns sharply when it is slow and gently when it is fast, and less in the air than on the snow.
7. When the run ends, **CONTINUE** returns to the start. Coins and upgrade levels stay.

The sled is the object named `Slider`. The road is the `Level1_Road` prefab.

## What the run contains

- Launch from a slingshot, then a physics slide down a spline road. The body does not roll. Friction comes from the snow mask: packed snow is slower, cleared ice is faster.
- Steering with a floating stick.
- Coins along the path. Driving through one collects it.
- Sounds from the Ladybug pack: obstacle impacts, her voice on a hit, the sit-down, a sharp turn on the snow, a landing, a coin pickup and button taps.
- Three obstacles, placed by hand in the scene. An iceberg and an electric gate end the run. A frozen bench keeps about a third of the current speed. Prefabs are in `Assets/_Game/Prefabs/Obstacles/Crash` and `.../Slow`.
- Street props from the Ladybug pack along both sides of the run, and each one is an obstacle. Buses, news vans and hedges end the run. Flower beds, flower carts, barriers and pizza signs break and keep about a third of the speed. Only the props near the sled are switched on.
- The run also ends if the sled stops, if it starts sliding backward, or when it reaches the finish.
- The finish is **70% of the spline**, not a shorter mesh. `finishFraction` on `Slider` changes that. Set it to `1` to require the whole road.
- After the run, a results card shows distance and coins earned. Upgrades bought before the next launch are saved with `PlayerPrefs`: slingshot power, lower sled friction, and coins paid per kilometre.
- Each upgrade has five looks. Every five levels fill a row of pips, and the next purchase changes the picture: a wooden slingshot becomes a golden one, a blue tube a jewelled one, a coin a treasure chest. On the fifth look with a full row the upgrade is maxed.

## Project layout

Everything the game uses is under `Assets/_Game`.

- `Scenes` holds the one scene, `Settings` the render pipeline and input assets.
- `Scripts` holds the code, split by layer:
  - `Core` is plain C#: the coin and upgrade rules, and the slingshot aim maths.
  - `Data` is the saved progress and the repository that reads and writes it.
  - `Game` is the objects of the scene. `Game/Sled` is the sled and its rider, `Game/Road` the road and what is built from it, `Game/Obstacles` the obstacle types. Coins, scenery, camera, sound and the composition root `GameBootstrap` sit next to them.
  - `UI` is the HUD and the steer stick.
  - `Editor` is the tools below.
- `Art` holds the Ladybug model and clips, the obstacle and street prop meshes, the road texture and the HUD sprites. `Audio` holds the sound clips and the `RunSounds` asset that lists them.
- `Prefabs` holds the road, the obstacles and the scenery. `Materials` and `Shaders` hold the snow road shader and the flat materials.

`TechDesign.md` describes the architecture. `.editorconfig` holds the formatting rules of the code.

## Tools

Five tools were written for this project. All of them work in the editor only.

### Road Sculpt

Raises and lowers the road surface with a round brush, so the track gets bumps, banks and hills.

- Select the road, then pick **Road Sculpt** in the tool strip of the Scene view.
- Drag on the road to raise it. Hold **Shift** to lower it instead.
- The panel in the Scene view sets the brush radius and how many meters per second it moves the surface. **Clear sculpt** flattens the whole road.
- The relief is saved with the road. The collider and the visible surface are rebuilt as you paint.

### Road Snow

Paints the snow mask: where the road is packed snow and where it is cleared ice. Ice has less friction, so a cleared lane is a fast lane.

- Select the road, then pick **Road Snow** in the tool strip of the Scene view.
- Drag on the road to clear the snow. Hold **Shift** to put it back.
- The panel sets the brush radius and strength. **Fill snow** covers the whole road, **Clear all snow** bares it.
- The mask is saved with the road. In Play Mode the sled clears its own trail into the same mask, and that trail is not saved.

### Snap Selection To Road

Seats hand-placed objects on the road and tilts them to the slope. Place props roughly, select them, and run one of the two commands:

| Command | Shortcut | What it does |
|---------|----------|--------------|
| **Tools → Road → Snap Selection To Road (Keep Facing)** | Cmd+Alt+R | Puts the object on the surface and tilts it to the normal. The way it is turned stays. |
| **Tools → Road → Snap Selection To Road (Face Along Road)** | Cmd+Alt+Shift+R | The same, and turns the object's forward axis down the road. |

**Tools → Road → Snap Settings** opens a small window with the same two buttons and two options: **Sink** pushes the object into the snow by a number of meters, and **Seat By Mesh Bottom** uses the lowest point of the meshes for models whose pivot is not at the base. One Undo step reverts the whole selection. An object that is not over the road is skipped and named in the Console.

### Game cheats

Shortcuts for testing the shop, under **Tools → Game**. They work in Edit Mode and in Play Mode. In Play Mode the coin counter and the shop update at once.

| Command | What it does |
|---------|--------------|
| **Clear Saved Progress** | Deletes every `PlayerPrefs` value of the project: coins, upgrade levels and the run count. Asks first. |
| **Add 1 000 Coins** | Adds 1 000 coins. |
| **Add 100 000 Coins** | Adds 100 000 coins. |

### Style check

`.editorconfig` holds the formatting rules a tool can check: braces on every branch, explicit access modifiers, and block bodies for methods. To check the code, open the project in Unity once so it writes `Assembly-CSharp.csproj`, then run this from the project folder:

```
dotnet format style Assembly-CSharp.csproj --verify-no-changes --no-restore --severity warn --diagnostics IDE0011 IDE0040 IDE0022
```

Remove `--verify-no-changes` to let it fix what it finds.

## Implementation decisions

- The road is generated from a Unity spline. Width, bowls, relief and the snow mask live on `SplineRoad`. Building meshes from that data is left to three helpers: `RoadMeshBuilder`, `RoadVisualChunks` and `RoadWalls`.
- The sled is a rigidbody with a capsule collider that lies along the direction of travel, because she rides lying down. The solver never rotates it: the heading and the slope set its rotation, so the character cannot tumble when the slope changes.
- The sled is split by job. `Sled` owns the body and the state of the run. `SlingshotPull` reads the pull, `SledSteering` turns the stick into a heading change, and `SnowTrail` clears the track and throws up the spray.
- Steering works from a sideways acceleration limit, not a fixed turn radius. The turn rate is that limit divided by the speed, which is why the sled answers quickly at low speed and stays stable when it is fast. A heading cone keeps it from turning across the road.
- Ladybug is parented to the sled and animated with clips from the pack through a Playable graph. She stands facing the camera on the start screen, turns and sits down on **TAP TO PLAY**, slides, and falls when she crashes. The results card waits two seconds for that fall.
- Coins are placed in code along the spline and only activated near the sled. The ribbon is several kilometres long, so keeping every prop awake was too expensive.
- Obstacles are prefabs placed by hand under the `Obstacles` object of the scene, so the level layout is designed rather than generated. An obstacle outside that object does not come back after a restart. `Obstacle` is the shared base: it checks the hit, fires once per run and resets on the next run. `CrashObstacle` and `SlowObstacle` only define the effect, so a new obstacle type is one small subclass.
- Obstacles are solid colliders, and the sled reports the touch from its own collision callback. A jump can clear an obstacle, and the collider can sit on any child of the prefab.
- Sounds are played by one listener, `RunAudio`. The sled, the coins and the HUD only raise events, and the clips live in one `RunSounds` asset.
- `GameBootstrap` is the one composition root. It loads the saved progress, hands the economy to the sled and the HUD, and connects the events. Nothing looks other objects up at runtime: references are set in the inspector.
- The HUD matches the Sled Surfers reference: pull tension, distance, speed along the surface, and progress to the finish. Its hierarchy lives in the scene, and `RideHud` only writes the numbers into it. It is not built from the Ladybug UI textures.
- Coins from a run are distance pay plus coins picked up. A retry does not add that payout a second time. Spending coins on upgrades persists between sessions.

## With more time

- Ride the full spline as the finish, and block the end with a readable gate instead of the 70% test line.
- Pose and scale Ladybug against the sled in the editor.
- Replace the code-built HUD with Ladybug UI.
- Add a steering-response upgrade. Launch power, sled friction, and income are the three that exist now.
- Dress the run with winter scenery. The street props are summer ones standing on the snow.
- Split the snow mask and its texture out of `SplineRoad`, which is still the largest class.
- Ship a player build and test it on a device.
