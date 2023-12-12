export interface OcadDiffDto {
  edited: EditedSymbolDto[];
  added: AddedSymbolDto[];
  deleted: DeletedSymbolDto[];
}

export interface EditedSymbolDto extends ISymbol, ISymbolMetadata {
  pointSymbolDiff?: PointSymbolDiff | null;
  lineSymbolDiff?: LineSymbolDiff | null;
  areaSymbolDiff?: AreaSymbolDiff | null;
}

export interface AddedSymbolDto extends ISymbol, ISymbolMetadata {}

export interface DeletedSymbolDto extends ISymbol, ISymbolMetadata {}

export interface PointSymbolDiff {
  movementInMeters: number;
}

export interface LineSymbolDiff {
  lengthDiffInPercent: number;
}

export interface AreaSymbolDiff {
  areaDiffInPercent: number;
}

export interface ISymbol {
  symbolNumber: string;
  symbolName: string;
}

export interface ISymbolMetadata {
  // Nullable because some symbols have invalid dates from OCAD
  createdAtUtc?: Date;
  lastEditedAtUtc?: Date;
}
