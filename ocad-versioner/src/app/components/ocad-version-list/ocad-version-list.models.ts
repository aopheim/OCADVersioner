export interface OcadVersionListDto {
  title?: string;
  description?: string;
  versionCreatedAt: Date;
  versionNumber: number;
  numberOfAddedSymbols: number;
  numberOfEditedSymbols: number;
  numberOfDeletedSymbols: number;
}
