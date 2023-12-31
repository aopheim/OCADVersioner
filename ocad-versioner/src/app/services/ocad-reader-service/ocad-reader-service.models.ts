import { FeatureCollection } from 'geojson';
import { OcadFile } from 'ocad2geojson-test';

export interface IOcadReaderService {
  getOcdObjectFromOcdFile(ocadFileHandle: File): Promise<OcadFile>;
  getGeoJsonFromOcadObject(ocadObject: OcadFile): Promise<FeatureCollection>;
  getGeoJsonFromOcdFile(ocadFileHandle: File): Promise<FeatureCollection>;
  getSvgFromOcadObject(ocadFileHandle: File): Promise<SVGElement>;
}
