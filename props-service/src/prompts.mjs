/**
 * Concept-image prompts for the avatar and its props. The framing rules are
 * Death Muffin's Tripo input rules (props: one object, ¾ view; characters:
 * front view, strict T-pose, empty hands; both on plain light grey with flat
 * light), which reliably produce clean, riggable models. The style line is
 * VTuber-friendly instead of dark fantasy.
 */

/**
 * Where a prop sits on the avatar, and how to draw it so Tripo gets the useful side.
 * `avatar` is a humanoid character (rigged by Tripo). `object` is an object avatar (a steak, a cactus):
 * no Tripo rig, because the app's automatic bend rig roots its base and bends it toward the face.
 */
export const SLOTS = {
  avatar:
    'Single full-body character, FRONT VIEW, strict T-pose: arms straight out horizontally at shoulder height, ' +
    'EMPTY hands (no props, no weapons, no magic), legs straight and slightly apart. Entire body visible head to feet, centered',
  object:
    'Single object standing upright on its own base, FRONT VIEW facing the camera straight on, roughly symmetrical, ' +
    'taller than it is wide, the whole object visible and centered',
  head: 'shown on its own (not worn), upright as it would sit on a head, three-quarter front view from slightly above',
  face: 'shown on its own (not worn), front three-quarter view at eye level',
  hand: 'shown on its own (not held), standing upright, three-quarter view',
  shoulder: 'shown on its own, three-quarter front view from slightly above',
  float: 'shown on its own, three-quarter view from slightly above',
};

export const DEFAULT_STYLE =
  'Stylized, clean and colourful, smooth chunky shapes with a bold readable silhouette, looks good next to an anime-style VTuber model.';

/** Characters and object avatars are drawn tall (3:4) so they fill the frame; props are square. */
export const conceptAspect = (prop) => (prop.slot === 'avatar' || prop.slot === 'object' ? '3:4' : '1:1');

export function conceptPrompt(prop, style = DEFAULT_STYLE) {
  if (prop.slot === 'object') {
    // No face on purpose: the app adds animated eyes and a mouth (or the streamer's own face) on the front.
    return (
      `3D modeling reference image of a goofy object VTuber avatar. ${SLOTS.object}. ` +
      'Plain flat light grey background (#c8c8c8), even neutral studio lighting, no shadows, no glow, no text, no other objects. ' +
      'NO face: no eyes, no mouth, no facial features at all, leave the upper front surface plain. No arms or legs unless stated. ' +
      `${style} Object: ${prop.subject}.`
    );
  }
  if (prop.slot === 'avatar') {
    // The simple flat face is deliberate: in the app, eyes and mouth become 2D stickers that swap per expression.
    return (
      `Character source for 3D modeling of a VTuber avatar. ${SLOTS.avatar}. ` +
      'Plain flat light grey background (#c8c8c8), even neutral studio lighting, no shadows, no glow, no text, no other characters. ' +
      'Stylized cartoon proportions: slightly large head, clearly separated arms and legs. ' +
      'Simple flat cartoon face: two big simple eyes and a small mouth. ' +
      `${style} Character: ${prop.subject}.`
    );
  }
  return (
    `3D modeling reference image of a single isolated accessory for a streamer's avatar, ${SLOTS[prop.slot]}, ` +
    'the whole object visible and centered, plain flat light grey background (#c8c8c8), even neutral studio lighting, ' +
    'no cast shadows, no ground plane, no text, no person, no mannequin, no other objects. ' +
    `${style} Subject: ${prop.subject}.`
  );
}
