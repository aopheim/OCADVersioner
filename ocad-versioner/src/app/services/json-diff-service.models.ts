import { OcadDiffDto } from '../components/ocad-diff-table/ocad-diff-table.models';

export interface IJsonDiffService {
  getJsonDiff(oldJsonData: string, newJsonData: string): OcadDiffDto;
}
