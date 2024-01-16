import { FeatureCollection } from 'geojson';

export interface JsonDiffServiceInput {
  oldVersion: FeatureCollection;
  newVersion: FeatureCollection;
}
