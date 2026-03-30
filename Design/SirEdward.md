# Sir Edward

## Regular attacks
- **horizontal**: Animated as a vertical swing in the direction the character is facing. 
- **down air**: Hold the axe pointed straight down and stabing it below him. 
- **up**: Swing in an overhead arc

## Specials
- **Neutral**: pray -- Edward kneels, and restores 2 HP, half a hit.
  - Timer, longer timer for recovery from doing this i.e. can't spam it.
- **up**: Throw the halberd at a 20 degree ish angle some distance, and teleport Edward to it. If it stops early by colliding with ground, or a character, teleport edward and deal damage if applicable. 
- **horizontal**: Pike formation -- Edward again kneels, but holds his halbered pointed infront of him, making his hurtbox smaller, but his weapons hitbox larger, enemies who charge at him have to get over his pike to attack him, or they will run into his halbered. 

## Coding challenges:
- Spawning and despawning hitboxes
- Giving knockback to others on attacks.
- Up special's near instantaneous movement. 
- handling knockback on damage taken. 
  - Can't have the character turn when getting hit. they should face their attacker.
- handle dodge i-frames and movement. 
