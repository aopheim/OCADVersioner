import { FeatureCollection } from 'geojson';
import { OcadDiffDto } from '../components/ocad-diff-table/ocad-diff-table.models';

export interface IJsonDiffService {
  getJsonDiff(
    oldJsonData: FeatureCollection,
    newJsonData: FeatureCollection
  ): OcadDiffDto;
}
