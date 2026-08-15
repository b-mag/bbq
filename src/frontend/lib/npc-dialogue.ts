/**
 * Overworld NPC dialogue — edit freely.
 *
 * Keys match world-object / enemy `type` (npc_cassilda, npc_fisher, …).
 * `lines` are shown one page at a time; E or click advances, Escape closes.
 * No quest flags yet — this is flavor and atmosphere only.
 */

export interface NpcDialogue {
  name: string;
  lines: string[];
}

export const NPC_DIALOGUE: Record<string, NpcDialogue> = {
  npc_cassilda: {
    name: 'Cassilda',
    lines: [
      'Along the shore the twin suns sink, and strange moons circle through the skies of Carcosa.',
      'Song of my soul, my voice is dead; die thou, unsung, as tears unshed.',
      'If the lake ever drinks itself dry, the house in the middle still sells what the drowned will not.',
    ],
  },
  npc_widow: {
    name: 'The Dock Widow',
    lines: [
      'West Hamlet used to fish. Now we wait for the water to decide what we are.',
      'Do not sleep facing the lake. It looks back.',
    ],
  },
  npc_ferryman: {
    name: 'Pallid Ferryman',
    lines: [
      'I do not take passengers. I take the ones the shore refuses.',
      'When the matchmaking of worlds aligns, Hali recedes. Then you may walk where boats fear.',
    ],
  },
  npc_ashwalker: {
    name: 'Ash Walker',
    lines: [
      'The Waste remembers fire. Your boots will too.',
      'Keep to the dunes. The Court of the Dragon still breathes under the ash.',
    ],
  },
  npc_marsh: {
    name: 'Marsh Whisperer',
    lines: [
      'Yhtill does not drown you. It invites you to stay.',
      'The bubbles are not fish. Do not answer them.',
    ],
  },
  npc_ranger: {
    name: 'Hyades Ranger',
    lines: [
      'The Dark Forest keeps its own roads. Mine are a courtesy.',
      'If the trees lean toward you, you are already late.',
    ],
  },
  npc_priest: {
    name: 'Pallid Priest',
    lines: [
      'The Yellow Palaces were never abandoned. They were finished.',
      'Do not read the signs. Reading is how the King enters.',
    ],
  },
  npc_hermit: {
    name: 'Star Hermit',
    lines: [
      'Climb. The black stars do not move, but they watch.',
      'At the peak the sky is closer than the village. That is not a comfort.',
    ],
  },
  npc_ember: {
    name: 'Ember Cantor',
    lines: [
      'The Court of the Dragon is a throat. We live in the cough.',
      'Bring no banners. The ash eats dyes first.',
    ],
  },
  npc_shopkeep: {
    name: 'The Intact Merchant',
    lines: [
      'Lake or no lake, Cryptol spends. The house does not flood. I do not ask why.',
      'Take what you need. The drowned have no use for boots.',
    ],
  },
  npc_fisher: {
    name: 'Dockhand',
    lines: [
      'Tide\'s wrong. Always is, this close to Hali.',
    ],
  },
  npc_villager: {
    name: 'Villager',
    lines: [
      'Keep your voice down after dusk. The houses listen better than we do.',
    ],
  },
  npc_monk: {
    name: 'Yellow Acolyte',
    lines: [
      'Have you seen the play? No. Good. Do not.',
    ],
  },
  npc_satyr: {
    name: 'Mask-Goat',
    lines: [
      'The hooves remember a forest that is not this one.',
      'Do not ask what the mask covers. It covers the asking.',
    ],
  },
  npc_maskbearer: {
    name: 'Horned Cantor',
    lines: [
      'We wore faces before we wore names.',
      'If the pale oval turns toward you, bow. If it smiles, you imagined it.',
    ],
  },
};

export function dialogueFor(type: string): NpcDialogue | null {
  if (NPC_DIALOGUE[type]) return NPC_DIALOGUE[type];
  if (type.startsWith('npc_')) {
    return { name: 'Wanderer', lines: ['...'] };
  }
  return null;
}

export const ENTERABLE_BUILDINGS = new Set([
  'organic_house',
  'giger_house',
  'mud_hut',
  'house',
  'dark_tower',
  'lake_shop',
]);

export function buildingKind(type: string): 'house' | 'hut' | 'tower' | 'shop' | 'cave' {
  if (type === 'lake_shop') return 'shop';
  if (type === 'dark_tower') return 'tower';
  if (type === 'mud_hut' || type === 'giger_house') return 'hut';
  if (type.includes('cave')) return 'cave';
  return 'house';
}
