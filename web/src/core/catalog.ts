/**
 * Card catalog for TCG Card Shop Simulator (game v1.02).
 *
 * How the game indexes cards (CPlayerData.GetCardSaveIndex):
 *   saveIndex = shownPos * perMonster + borderType + (isFoil ? perMonsterNoFoil : 0)
 * where shownPos is the monster's position in the expansion's "shown monster list",
 * perMonsterNoFoil is 6 (1 for Ghost) and perMonster is twice that.
 *
 * Tetramon, Destiny and Ascension share one shown list; Ghost has its own. The shown lists
 * are game asset data. Community tools and the game's monster enum agree that the main list
 * is EMonsterType 1..121 in order and that the Ghost list is the 20 Ghost monsters in enum
 * order. Keep those orders in SHOWN_MAIN / SHOWN_GHOST so they can be corrected in one
 * place if a future game update reorders them.
 */

export type Tier = 'Common' | 'Rare' | 'Epic' | 'Legendary';
export const TIERS: readonly Tier[] = ['Common', 'Rare', 'Epic', 'Legendary'];

export interface Monster {
  /** EMonsterType value. */
  id: number;
  name: string;
  tier: Tier;
}

const T: Record<string, Tier> = { C: 'Common', R: 'Rare', E: 'Epic', L: 'Legendary' };

// EMonsterType 1..121, display names and pack rarity.
const MAIN_MONSTERS: ReadonlyArray<readonly [string, string]> = [
  ['Pigni', 'C'], ['Burpig', 'R'], ['Inferhog', 'E'], ['Blazoar', 'L'],
  ['Kidsune', 'C'], ['Bonfiox', 'R'], ['Honobi', 'E'], ['Kyuenbi', 'L'],
  ['Nanomite', 'C'], ['Decimite', 'R'], ['Meganite', 'E'], ['Giganite', 'L'],
  ['Sapoling', 'C'], ['Forush', 'R'], ['Timbro', 'E'], ['Mammotree', 'L'],
  ['Minstar', 'C'], ['Trickstar', 'R'], ['Princestar', 'E'], ['Kingstar', 'L'],
  ['Shellow', 'C'], ['Clamigo', 'R'], ['Aquariff', 'E'], ['Fistronk', 'L'],
  ['Wurmgle', 'C'], ['Pupazz', 'R'], ['Mothini', 'E'], ['Royalama', 'L'],
  ['Nocti', 'C'], ['Lunight', 'R'], ['Vampicant', 'E'], ['Dracunix', 'L'],
  ['Minotos', 'R'], ['Drilceros', 'E'], ['Grizzaw', 'E'], ['Jelicleen', 'R'],
  ['Wispo', 'R'], ['Mummog', 'R'], ['Helio', 'C'], ['Pixy', 'R'],
  ['Flory', 'E'], ['Magnoria', 'L'], ['Werboo', 'C'], ['Flami', 'C'],
  ['Angez', 'R'], ['Moskit', 'E'], ['Kyrone', 'C'], ['Twofrost', 'R'],
  ['Threeze', 'E'], ['Hydroid', 'L'], ['Drakon', 'L'], ['Bogon', 'L'],
  ['Hydron', 'L'], ['Raizon', 'L'], ['Tortugor', 'E'], ['Lupup', 'C'],
  ['Luphire', 'R'], ['Lucinder', 'E'], ['Lucadence', 'L'], ['Gupi', 'C'],
  ['Sharfin', 'R'], ['Gilgabass', 'E'], ['Jigajawr', 'L'], ['Batrang', 'C'],
  ['Dusko', 'R'], ['Wolgin', 'E'], ['Jacktern', 'L'], ['Tetron', 'C'],
  ['Raxx', 'R'], ['Gannon', 'E'], ['GigatronX', 'L'], ['Clawop', 'C'],
  ['Clawdos', 'R'], ['Clawaken', 'E'], ['Clawcifear', 'L'], ['Sunflork', 'C'],
  ['Scarlios', 'R'], ['Scarkgorus', 'E'], ['Crobib', 'C'], ['Crosilisk', 'R'],
  ['Crorathian', 'E'], ['Nimblis', 'C'], ['Nimboculo', 'R'], ['Nimbustrike', 'E'],
  ['Esmeri', 'C'], ['Esmerock', 'R'], ['Esmerdios', 'E'], ['Litspire', 'E'],
  ['Voltrex', 'L'], ['Crablox', 'R'], ['Clawvenger', 'E'], ['Flambrolly', 'L'],
  ['Lumie', 'R'], ['Seedant', 'C'], ['Budwing', 'R'], ['Buzzeed', 'E'],
  ['Beakai', 'R'], ['Talontsu', 'E'], ['Talonika', 'E'], ['Talonryu', 'L'],
  ['Kataryu', 'E'], ['Katengu', 'L'], ['Mufflin', 'C'], ['Muffleur', 'R'],
  ['Mufflimax', 'E'], ['Anguifish', 'C'], ['Amneshark', 'R'], ['Amnesilla', 'E'],
  ['Frizard', 'R'], ['Gekoflare', 'E'], ['Terradrakon', 'L'], ['Flamchik', 'C'],
  ['Pyropeck', 'R'], ['Poseia', 'C'], ['Posteed', 'R'], ['Poseigon', 'E'],
  ['Poseidrake', 'L'], ['Sludglop', 'C'], ['Sludgetox', 'R'], ['Toxigoop', 'E'],
  ['Toximuck', 'L'],
];

export const MONSTERS: ReadonlyMap<number, Monster> = new Map(
  MAIN_MONSTERS.map(([name, t], i) => [i + 1, { id: i + 1, name, tier: T[t]! }]),
);

/** m_ShownMonsterList: Tetramon, Destiny and Ascension. */
export const SHOWN_MAIN: readonly number[] = MAIN_MONSTERS.map((_, i) => i + 1);
/** m_ShownGhostMonsterList. */
export const SHOWN_GHOST: readonly number[] = [
  4, 8, 12, 16, 20, 24, 28, 32, 42, 50, 51, 52, 53, 54, 59, 63, 67, 71, 75, 102,
];

/** ECardExpansionType values. */
export const ExpansionType = {
  Tetramon: 0,
  Destiny: 1,
  Ghost: 2,
  Megabot: 3,
  FantasyRPG: 4,
  CatJob: 5,
  FoodieGO: 6,
  Ascension: 7,
} as const;

export type SetKey = 'tetramon' | 'destiny' | 'ascension' | 'ghost' | 'megabot' | 'fantasyrpg' | 'catjob';

/**
 * One physical list family in the save. Most sets have one; Ghost has White (isDestiny=false)
 * and Black (isDestiny=true) halves, each with its own index space.
 */
export interface ListDef {
  /** Suffix on m_CardCollectedList / m_IsCardCollectedList / m_CardPriceSetList. */
  suffix: string;
  /**
   * Suffix on m_GenCardMarketPriceList for this half. The game routes Ghost market prices the
   * opposite way round from collected counts: Black (isDestiny) reads ...Ghost and White
   * reads ...GhostBlack.
   */
  marketSuffix: string;
  isDestiny: boolean;
  /** Drill-down group label for sets split by list (Ghost). */
  label?: string;
}

export interface SetDef {
  key: SetKey;
  name: string;
  expansionType: number;
  lists: ListDef[];
  shown: readonly number[];
  /** Border slots per monster before foil (6, or 1 for Ghost). */
  perMonsterNoFoil: number;
  /** Edition names for borderType 0..perMonsterNoFoil-1. */
  editions: readonly string[];
  /** Not obtainable in the unmodded game; only shown when the save has cards for it. */
  hiddenUnlessOwned?: boolean;
  monsterName?: (monsterId: number, shownPos: number) => string;
}

const STANDARD_EDITIONS = ['Basic', 'First Edition', 'Silver', 'Gold', 'EX', 'Full Art'] as const;
// Ascension reuses the six border slots with different treatments.
const ASCENSION_EDITIONS = ['Basic', 'Silver', 'Gold', 'Silver Full Art', 'EX Full Art', 'Borderless Full Art'] as const;

function range(start: number, count: number): number[] {
  return Array.from({ length: count }, (_, i) => start + i);
}

export const SETS: readonly SetDef[] = [
  {
    key: 'tetramon',
    name: 'Tetramon',
    expansionType: ExpansionType.Tetramon,
    lists: [{ suffix: '', marketSuffix: '', isDestiny: false }],
    shown: SHOWN_MAIN,
    perMonsterNoFoil: 6,
    editions: STANDARD_EDITIONS,
  },
  {
    key: 'destiny',
    name: 'Destiny',
    expansionType: ExpansionType.Destiny,
    lists: [{ suffix: 'Destiny', marketSuffix: 'Destiny', isDestiny: true }],
    shown: SHOWN_MAIN,
    perMonsterNoFoil: 6,
    editions: STANDARD_EDITIONS,
  },
  {
    key: 'ascension',
    name: 'Ascension',
    expansionType: ExpansionType.Ascension,
    lists: [{ suffix: 'Ascension', marketSuffix: 'Ascension', isDestiny: false }],
    shown: SHOWN_MAIN,
    perMonsterNoFoil: 6,
    editions: ASCENSION_EDITIONS,
  },
  {
    key: 'ghost',
    name: 'Ghost',
    expansionType: ExpansionType.Ghost,
    lists: [
      { suffix: 'Ghost', marketSuffix: 'GhostBlack', isDestiny: false, label: 'White' },
      { suffix: 'GhostBlack', marketSuffix: 'Ghost', isDestiny: true, label: 'Black' },
    ],
    shown: SHOWN_GHOST,
    perMonsterNoFoil: 1,
    editions: ['Full Art'],
  },
  // Unreleased sets that exist in the game files and are enabled by content mods.
  // Monster names are not in the save, so cards are labelled by number.
  {
    key: 'megabot',
    name: 'Megabot',
    expansionType: ExpansionType.Megabot,
    lists: [{ suffix: 'Megabot', marketSuffix: 'Megabot', isDestiny: false }],
    shown: range(1000, 113),
    perMonsterNoFoil: 6,
    editions: STANDARD_EDITIONS,
    hiddenUnlessOwned: true,
    monsterName: (_id, pos) => `Megabot #${pos + 1}`,
  },
  {
    key: 'fantasyrpg',
    name: 'Fantasy RPG',
    expansionType: ExpansionType.FantasyRPG,
    lists: [{ suffix: 'FantasyRPG', marketSuffix: 'FantasyRPG', isDestiny: false }],
    shown: range(2000, 50),
    perMonsterNoFoil: 6,
    editions: STANDARD_EDITIONS,
    hiddenUnlessOwned: true,
    monsterName: (_id, pos) => `Fantasy RPG #${pos + 1}`,
  },
  {
    key: 'catjob',
    name: 'Cat Job',
    expansionType: ExpansionType.CatJob,
    lists: [{ suffix: 'CatJob', marketSuffix: 'CatJob', isDestiny: false }],
    shown: range(3000, 40),
    perMonsterNoFoil: 6,
    editions: STANDARD_EDITIONS,
    hiddenUnlessOwned: true,
    monsterName: (_id, pos) => `Cat Job #${pos + 1}`,
  },
];

export interface CardDef {
  /** Position in CATALOG.cards; the key used by every count array. */
  i: number;
  set: SetKey;
  /** Index into the set's lists (Ghost: 0 White, 1 Black). */
  list: number;
  saveIndex: number;
  monsterId: number;
  /** 1-based number within the set's shown list (the binder number). */
  num: number;
  name: string;
  tier: Tier;
  edition: number;
  editionName: string;
  foil: boolean;
  /** Drill-down level 1: the edition, or White/Black for Ghost. */
  group: string;
}

export interface Catalog {
  cards: CardDef[];
  sets: readonly SetDef[];
  /** cards of a set, in save-index order (lists concatenated). */
  bySet: Map<SetKey, CardDef[]>;
  /** Group labels per set, in display order. */
  groups: Map<SetKey, string[]>;
  setByKey: Map<SetKey, SetDef>;
  /** Lookup: `${setKey}:${list}:${saveIndex}` -> card. */
  byIndex: Map<string, CardDef>;
}

/** Bump when catalog contents or ordering change, so stored snapshots can be re-validated. */
export const CATALOG_VERSION = 1;

function buildCatalog(): Catalog {
  const cards: CardDef[] = [];
  const bySet = new Map<SetKey, CardDef[]>();
  const groups = new Map<SetKey, string[]>();
  const byIndex = new Map<string, CardDef>();
  for (const set of SETS) {
    const setCards: CardDef[] = [];
    const per = set.perMonsterNoFoil * 2;
    set.lists.forEach((list, li) => {
      set.shown.forEach((monsterId, pos) => {
        const m = MONSTERS.get(monsterId);
        for (let v = 0; v < per; v++) {
          const foil = v >= set.perMonsterNoFoil;
          const edition = v % set.perMonsterNoFoil;
          const card: CardDef = {
            i: cards.length,
            set: set.key,
            list: li,
            saveIndex: pos * per + v,
            monsterId,
            num: pos + 1,
            name: set.monsterName ? set.monsterName(monsterId, pos) : (m?.name ?? `#${monsterId}`),
            tier: m?.tier ?? 'Common',
            edition,
            editionName: set.editions[edition] ?? `Border ${edition}`,
            foil,
            group: list.label ?? set.editions[edition] ?? `Border ${edition}`,
          };
          cards.push(card);
          setCards.push(card);
          byIndex.set(`${set.key}:${li}:${card.saveIndex}`, card);
        }
      });
    });
    bySet.set(set.key, setCards);
    groups.set(set.key, set.lists.length > 1 ? set.lists.map((l) => l.label ?? l.suffix) : [...set.editions]);
  }
  return { cards, sets: SETS, bySet, groups, setByKey: new Map(SETS.map((s) => [s.key, s])), byIndex };
}

export const CATALOG: Catalog = buildCatalog();

export function setForExpansion(expansionType: number): SetDef | undefined {
  return SETS.find((s) => s.expansionType === expansionType);
}

/** List half for an expansion + isDestiny flag. isDestiny only matters for Ghost. */
export function listIndexFor(set: SetDef, isDestiny: boolean): number {
  if (set.lists.length === 1) return 0;
  return isDestiny ? 1 : 0;
}

/** Resolve a physical CardData identity to a catalog card, or undefined if unknown. */
export function cardFromPhysical(
  expansionType: number,
  monsterType: number,
  borderType: number,
  isFoil: boolean,
  isDestiny: boolean,
): CardDef | undefined {
  const set = setForExpansion(expansionType);
  if (!set) return undefined;
  const pos = set.shown.indexOf(monsterType);
  if (pos < 0) return undefined;
  // Ghost cards are always stored with border 0.
  const border = set.perMonsterNoFoil === 1 ? 0 : borderType;
  if (border < 0 || border >= set.perMonsterNoFoil) return undefined;
  const saveIndex = pos * set.perMonsterNoFoil * 2 + border + (isFoil ? set.perMonsterNoFoil : 0);
  return CATALOG.byIndex.get(`${set.key}:${listIndexFor(set, isDestiny)}:${saveIndex}`);
}

/** Resolve a compact (expansionType, cardSaveIndex, isDestiny) record to a catalog card. */
export function cardFromCompact(expansionType: number, cardSaveIndex: number, isDestiny: boolean): CardDef | undefined {
  const set = setForExpansion(expansionType);
  if (!set) return undefined;
  return CATALOG.byIndex.get(`${set.key}:${listIndexFor(set, isDestiny)}:${cardSaveIndex}`);
}

export function cardLabel(c: CardDef): string {
  const parts = [c.name];
  if (c.set === 'ghost') parts.push(c.group);
  else parts.push(c.editionName);
  if (c.foil) parts.push('Foil');
  return parts.join(' · ');
}

export const GRADE_NAMES: readonly string[] = [
  '',
  'Very Poor',
  'Poor',
  'Fair',
  'Good',
  'Very Good',
  'Fine',
  'Excellent',
  'Near Mint',
  'Mint',
  'Gem Mint',
];
