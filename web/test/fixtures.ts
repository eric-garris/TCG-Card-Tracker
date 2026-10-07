/** Builders for synthetic save files shaped like the game's JsonUtility output. */

export interface PhysicalCard {
  monsterType: number;
  expansionType: number;
  borderType: number;
  isFoil: boolean;
  isDestiny: boolean;
  cardGrade: number;
  gradedCardIndex?: number;
}

export function physical(p: Partial<PhysicalCard> & { monsterType: number }): PhysicalCard {
  return { expansionType: 0, borderType: 0, isFoil: false, isDestiny: false, cardGrade: 0, ...p };
}

export function compact(expansionType: number, cardSaveIndex: number, amount: number, extra: { isDestiny?: boolean; gradedCardIndex?: number } = {}) {
  return { expansionType, cardSaveIndex, amount, gradedCardIndex: extra.gradedCardIndex ?? 0, isDestiny: extra.isDestiny ?? false };
}

/** An empty default CardData, as written for an empty display compartment. */
export const EMPTY_SLOT = physical({ monsterType: 0 });

export function padded<T>(values: T[], fill: T, length = 2100): T[] {
  return [...values, ...Array.from({ length: Math.max(0, length - values.length) }, () => fill)];
}

export function marketPrice(base: number, pct = 0) {
  return { generatedMarketPrice: base, pricePercentChangeList: pct, pastPricePercentChangeList: [] as number[] };
}

/** A minimal 1.0-era save with every list present but empty. */
export function emptySave(): Record<string, unknown> {
  const suffixes = ['', 'Destiny', 'Ghost', 'GhostBlack', 'Megabot', 'FantasyRPG', 'CatJob', 'Ascension'];
  const save: Record<string, unknown> = {
    m_PlayerName: 'Tester',
    m_CurrentDay: 12,
    m_ShopLevel: 9,
    m_CoinAmount: 1234.5,
    m_CoinAmountDouble: 1234.5,
    m_GradedCardInventoryList: [],
    m_CardShelfSaveDataList: [],
    m_CardItemCombiShelfSaveDataList: [],
    m_CardStorageShelfSaveDataList: [],
    m_BulkDonationSaveDataList: [],
    m_PackageBoxCardSaveDataList: [],
    m_AutoPackOpenerSaveDataList: [],
    m_DeckCompactCardDataList: [],
    m_HoldCardDataList: [],
    m_GradeCardInProgressList: [],
    m_CurrentGradeCardSubmitSet: { m_CardDataList: [], m_ServiceLevel: 0, m_DayPassed: 0, m_MinutePassed: 0 },
    m_GenGradedCardPriceMultiplierList: [],
  };
  for (const s of suffixes) {
    save['m_CardCollectedList' + s] = padded<number>([], 0);
    save['m_IsCardCollectedList' + s] = padded<boolean>([], false);
    save['m_GenCardMarketPriceList' + s] = [];
  }
  return save;
}

export function setAt<T>(list: T[], index: number, value: T): void {
  list[index] = value;
}
