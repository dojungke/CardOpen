# Pack Ascent prototype

Open `Assets/Scenes/SampleScene.unity` and press Play. The prototype is built automatically at runtime.

## Interaction

1. Drag the large 3D pack far enough in any direction to throw the wrapper away.
2. Tap the top face-down card to flip and reveal it.
3. Swipe the revealed card left or right to move to the next card.
4. After all five cards are swiped away, the pack score and combos are calculated.
5. Continue until the round target is cleared or all three packs are used.

## Scoring

- Card rarity determines base points.
- Three cards from the same family add 50% of the pack's base points.
- Every duplicate pair adds 30 points.
- Clearing a round grants one run-long upgrade.

All visuals use Unity cube primitives and shared untextured materials. Realtime shadows are disabled and only the current five-card stack is kept alive, keeping the scene suitable for an early WebGL build.
