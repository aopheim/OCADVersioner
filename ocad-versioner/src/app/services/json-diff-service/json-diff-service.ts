import { Injectable } from '@angular/core';
import {
  AddedSymbolDto,
  DeletedSymbolDto,
  EditedSymbolDto,
  OcadDiffDto,
  PointSymbolDiff,
} from '../../components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models';
import { isNil, isEqual, cloneDeep } from 'lodash-es';
import {
  Feature,
  FeatureCollection,
  GeoJsonProperties,
  Geometry,
  LineString,
  MultiLineString,
  MultiPoint,
  MultiPolygon,
  Point,
  Polygon,
} from 'geojson';
import { IofSymbolHelper } from '../iof-symbol-service/iof-symbol-service';
import { getAreaOfPolygon, getDistance, getPathLength } from 'geolib';
import { GeolibInputCoordinates } from 'geolib/es/types';
import { CoordinatesHelper } from '../coordinates-helper/coordinates-helper.service';

@Injectable()
export class JsonDiffService {
  // Interesting... https://knowledge.broadcom.com/external/article/57052/how-to-convert-unix-epoch-time-values-in.html
  private static readonly ExcelEpoch: number = 25569;

  public static calculateJsonDiff(
    old: FeatureCollection,
    newest: FeatureCollection,
    epsgCode: number = CoordinatesHelper.DefaultEpsgCode,
    reportProgress?: (progress: number) => void
  ): OcadDiffDto {
    const added: AddedSymbolDto[] = [];
    const edited: EditedSymbolDto[] = [];
    const deleted: DeletedSymbolDto[] = [];
    let oldFeatures = cloneDeep(old.features);
    const newFeatures = cloneDeep(newest.features);
    const reportEveryN = this.getReportEveryNFeature(newFeatures.length);
    const numberOfNewFeatures: number = newFeatures.length;
    newFeatures.forEach((newFeature, i) => {
      if (i % reportEveryN === 0 && reportProgress)
        reportProgress((i / numberOfNewFeatures) * 100 * 0.8);
      if (this.isChildFeature(newFeature)) return;
      const indexInOldFeatures = oldFeatures.findIndex((oldFeature) =>
        isSameFeature(oldFeature, newFeature)
      );
      const matchInOldFeatures =
        indexInOldFeatures > -1 ? oldFeatures[indexInOldFeatures] : null;
      if (isNil(matchInOldFeatures)) {
        added.push(this.convertToAddedSymbol(newFeature, epsgCode));
      } else {
        if (!this.areFeaturesEqual(newFeature, matchInOldFeatures))
          edited.push(
            this.convertToEditedSymbol(newFeature, matchInOldFeatures, epsgCode)
          );
        // The feature is either edited or untouched. Gives fewer possible deleted elements to search for.
        oldFeatures.splice(indexInOldFeatures, 1);
      }
    });

    const numberOfOldFeatures = oldFeatures.length;
    oldFeatures.forEach((oldFeature, i) => {
      if (i % reportEveryN === 0 && reportProgress)
        reportProgress(80 + (i / numberOfOldFeatures) * 100 * 0.2);

      if (this.isChildFeature(oldFeature)) return;
      const indexInNewFeatures = newFeatures.findIndex((newFeature) =>
        isSameFeature(oldFeature, newFeature)
      );
      if (indexInNewFeatures === -1)
        deleted.push(this.convertToDeletedSymbol(oldFeature, epsgCode));
    });
    if (reportProgress) reportProgress(100);

    return {
      added,
      deleted,
      edited,
    } as OcadDiffDto;

    function isSameFeature(
      oldFeature: Feature<Geometry, GeoJsonProperties>,
      newFeature: Feature<Geometry, GeoJsonProperties>
    ): unknown {
      return (
        oldFeature.id === newFeature.id &&
        (oldFeature.properties?.[OcadPropertyKeys.CreationDate] as number) ===
          (newFeature.properties?.[OcadPropertyKeys.CreationDate] as number)
      );
    }
  }
  private static getReportEveryNFeature(totalFeatures: number): number {
    if (totalFeatures > 0 && totalFeatures < 100) return 10;
    if (totalFeatures >= 100 && totalFeatures < 10000) return 100;
    return 100;
  }

  private static isChildFeature(
    feature: Feature<Geometry, GeoJsonProperties>
  ): boolean {
    const parentId = feature.properties?.[OcadPropertyKeys.ParentId];
    return !isNil(parentId) && parentId > 0;
  }

  private static areFeaturesEqual(
    a: Feature<Geometry, GeoJsonProperties>,
    b: Feature<Geometry, GeoJsonProperties>
  ): boolean {
    if (isNil(a) || isNil(b)) return false;
    if (a.id !== b.id) return false;
    if (
      a.properties?.[OcadPropertyKeys.Symbol] !==
      b.properties?.[OcadPropertyKeys.Symbol]
    )
      return false;
    if (a.geometry.type !== b.geometry.type) return false;
    switch (a.geometry.type) {
      case 'LineString':
        if (
          !this.geometriesAreEqual(
            a.geometry as LineString,
            b.geometry as LineString
          )
        )
          return false;
        break;
      case 'MultiLineString':
        if (
          !this.geometriesAreEqual(
            a.geometry as MultiLineString,
            b.geometry as MultiLineString
          )
        )
          return false;
        break;
      case 'MultiPoint':
        if (
          !this.geometriesAreEqual(
            a.geometry as MultiPoint,
            b.geometry as MultiPoint
          )
        )
          return false;
        break;
      case 'MultiPolygon':
        if (
          !this.geometriesAreEqual(
            a.geometry as MultiPolygon,
            b.geometry as MultiPolygon
          )
        )
          return false;
        break;
      case 'Point':
        if (!this.geometriesAreEqual(a.geometry as Point, b.geometry as Point))
          return false;
        break;
      case 'Polygon':
        if (
          !this.geometriesAreEqual(a.geometry as Polygon, b.geometry as Polygon)
        )
          return false;
        break;
      default: {
        console.warn('No comparison done for type ', a.geometry.type);
        return false;
      }
    }
    return true;
  }

  private static geometriesAreEqual(
    a:
      | LineString
      | MultiLineString
      | MultiPoint
      | MultiPolygon
      | Point
      | Polygon,
    b:
      | LineString
      | MultiLineString
      | MultiPoint
      | MultiPolygon
      | Point
      | Polygon
  ): boolean {
    return isEqual(a.coordinates, b.coordinates);
  }

  private static getDateFromExcelDate(excelDate: number): Date | undefined {
    const date = new Date(
      (excelDate - JsonDiffService.ExcelEpoch) * 86400 * 1000
    );
    return date instanceof Date && !isNaN(date.valueOf()) ? date : undefined;
  }

  private static convertToAddedSymbol(
    feature: Feature<Geometry, GeoJsonProperties>,
    epsgCode: number
  ): AddedSymbolDto {
    const symbolNameInGeoJson: number =
      feature.properties?.[OcadPropertyKeys.Symbol];
    const symbolName: string =
      IofSymbolHelper.getSymbolName(symbolNameInGeoJson);
    const symbolNumber: string =
      IofSymbolHelper.getSymbolNumber(symbolNameInGeoJson);
    const creationDateAsNumber: number =
      feature.properties?.[OcadPropertyKeys.CreationDate];
    const lastEditedAsNumber =
      feature.properties?.[OcadPropertyKeys.ModificationDate];
    const createdAtUtc = this.getDateFromExcelDate(creationDateAsNumber);
    let lastEditedAtUtc: Date | undefined =
      this.getDateFromExcelDate(lastEditedAsNumber);
    if (creationDateAsNumber === lastEditedAsNumber)
      lastEditedAtUtc = undefined;
    let symbolLengthInMeters: number | null = null;
    if (feature.geometry.type === 'LineString')
      symbolLengthInMeters = this.getLengthOfLine(feature, epsgCode);
    let symbolAreaInSquareMeters: number | null = null;
    if (feature.geometry.type === 'Polygon')
      symbolAreaInSquareMeters = this.getAreaOfPolygon(feature, epsgCode);
    return {
      createdAtUtc,
      lastEditedAtUtc,
      symbolName,
      symbolNumber,
      symbolLengthInMeters,
      symbolAreaInSquareMeters,
    };
  }

  private static convertToEditedSymbol(
    newFeature: Feature<Geometry, GeoJsonProperties>,
    oldFeature: Feature<Geometry, GeoJsonProperties>,
    epsgCode: number
  ): EditedSymbolDto {
    const symbolName: string = IofSymbolHelper.getSymbolName(
      newFeature.properties?.[OcadPropertyKeys.Symbol]
    );
    const symbolNumber: string = IofSymbolHelper.getSymbolNumber(
      newFeature.properties?.[OcadPropertyKeys.Symbol]
    );
    const pointSymbolDiff =
      newFeature.geometry.type === 'Point' &&
      oldFeature.geometry.type === 'Point'
        ? this.getPointSymbolDiff(newFeature, oldFeature, epsgCode)
        : null;
    return {
      createdAtUtc: this.getDateFromExcelDate(
        newFeature.properties?.[OcadPropertyKeys.CreationDate]
      ),
      lastEditedAtUtc: this.getDateFromExcelDate(
        newFeature.properties?.[OcadPropertyKeys.ModificationDate]
      ),
      symbolName,
      symbolNumber,
      areaSymbolDiff: null,
      lineSymbolDiff: null,
      pointSymbolDiff,
    };
  }

  private static getPointSymbolDiff(
    newFeature: Feature<Geometry, GeoJsonProperties>,
    oldFeature: Feature<Geometry, GeoJsonProperties>,
    epsgCode: number
  ): PointSymbolDiff {
    const newGeometry = newFeature.geometry as Point;
    const oldGeometry = oldFeature.geometry as Point;
    const movementInMeters = getDistance(
      CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
        oldGeometry.coordinates[0],
        oldGeometry.coordinates[1],
        epsgCode
      ),
      CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
        newGeometry.coordinates[0],
        newGeometry.coordinates[1],
        epsgCode
      )
    );
    return { movementInMeters };
  }

  private static getLengthOfLine(
    feature: Feature<Geometry, GeoJsonProperties>,
    epsgCode: number
  ): number {
    const geometry = feature.geometry as LineString;
    return getPathLength(
      geometry.coordinates.map((c) => {
        const latLongCoords =
          CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
            c[0],
            c[1],
            epsgCode
          );
        return {
          lon: latLongCoords.longitude,
          lat: latLongCoords.latitude,
        } as GeolibInputCoordinates;
      })
    );
  }

  private static getAreaOfPolygon(
    feature: Feature<Geometry, GeoJsonProperties>,
    epsgCode: number
  ): number | null {
    const geometry = feature.geometry as Polygon;

    const points: GeolibInputCoordinates[] = [];
    geometry.coordinates.forEach((positions) => {
      positions.forEach((p) => {
        const latLong = CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
          p[0],
          p[1],
          epsgCode
        );
        points.push({ lat: latLong.latitude, lon: latLong.longitude });
      });
    });
    return getAreaOfPolygon(points);
  }

  private static convertToDeletedSymbol(
    feature: Feature<Geometry, GeoJsonProperties>,
    epsgCode: number
  ): DeletedSymbolDto {
    const symbolName: string = IofSymbolHelper.getSymbolName(
      feature.properties?.[OcadPropertyKeys.Symbol]
    );
    const symbolNumber: string = IofSymbolHelper.getSymbolNumber(
      feature.properties?.[OcadPropertyKeys.Symbol]
    );
    let symbolLengthInMeters: number | null = null;
    if (feature.geometry.type === 'LineString')
      symbolLengthInMeters = this.getLengthOfLine(feature, epsgCode);
    let symbolAreaInSquareMeters: number | null = null;
    if (feature.geometry.type === 'Polygon')
      symbolAreaInSquareMeters = this.getAreaOfPolygon(feature, epsgCode);
    return {
      createdAtUtc: this.getDateFromExcelDate(
        feature.properties?.[OcadPropertyKeys.CreationDate]
      ),
      lastEditedAtUtc: this.getDateFromExcelDate(
        feature.properties?.[OcadPropertyKeys.ModificationDate]
      ),
      symbolName,
      symbolNumber,
      symbolAreaInSquareMeters,
      symbolLengthInMeters,
    };
  }
}

export enum OcadPropertyKeys {
  CreationDate = 'creationDate',
  ModificationDate = 'modificationDate',
  Symbol = 'sym',
  ParentId = 'parentId',
}
