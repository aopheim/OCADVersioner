import { Injectable } from '@angular/core';
import { IJsonDiffService } from './json-diff-service.models';
import {
  AddedSymbolDto,
  DeletedSymbolDto,
  EditedSymbolDto,
  OcadDiffDto,
  PointSymbolDiff,
} from '../../components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models';
import { isNil, isEqual, clone, cloneDeep } from 'lodash-es';
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
import { getDistance } from 'geolib';

@Injectable()
export class JsonDiffService implements IJsonDiffService {
  // Interesting... https://knowledge.broadcom.com/external/article/57052/how-to-convert-unix-epoch-time-values-in.html
  private readonly ExcelEpoch: number = 25569;

  getJsonDiff(old: FeatureCollection, newest: FeatureCollection): OcadDiffDto {
    const added: AddedSymbolDto[] = [];
    const edited: EditedSymbolDto[] = [];
    const deleted: DeletedSymbolDto[] = [];
    let oldFeatures = cloneDeep(old.features);
    const newFeatures = cloneDeep(newest.features);
    newFeatures.forEach((newFeature) => {
      if (this.isChildFeature(newFeature)) return;
      const indexInOldFeatures = oldFeatures.findIndex((oldFeature) =>
        isSameFeature(oldFeature, newFeature)
      );
      const matchInOldFeatures =
        indexInOldFeatures > -1 ? oldFeatures[indexInOldFeatures] : null;
      if (isNil(matchInOldFeatures)) {
        added.push(this.convertToAddedSymbol(newFeature));
      } else {
        if (!this.areFeaturesEqual(newFeature, matchInOldFeatures))
          edited.push(
            this.convertToEditedSymbol(newFeature, matchInOldFeatures)
          );
        // The feature is either edited or untouched. Gives fewer possible deleted elements to search for.
        oldFeatures.splice(indexInOldFeatures, 1);
      }
    });
    oldFeatures.forEach((oldFeature) => {
      if (this.isChildFeature(oldFeature)) return;
      const indexInNewFeatures = newFeatures.findIndex((newFeature) =>
        isSameFeature(oldFeature, newFeature)
      );
      if (indexInNewFeatures === -1)
        deleted.push(this.convertToDeletedSymbol(oldFeature));
    });

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

  private isChildFeature(
    feature: Feature<Geometry, GeoJsonProperties>
  ): boolean {
    const parentId = feature.properties?.[OcadPropertyKeys.ParentId];
    return !isNil(parentId) && parentId > 0;
  }

  private areFeaturesEqual(
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

  private geometriesAreEqual(
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

  private getDateFromExcelDate(excelDate: number): Date | undefined {
    const date = new Date((excelDate - this.ExcelEpoch) * 86400 * 1000);
    return date instanceof Date && !isNaN(date.valueOf()) ? date : undefined;
  }

  private convertToAddedSymbol(
    feature: Feature<Geometry, GeoJsonProperties>
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
    return {
      createdAtUtc,
      lastEditedAtUtc,
      symbolName,
      symbolNumber,
    };
  }

  private convertToEditedSymbol(
    newFeature: Feature<Geometry, GeoJsonProperties>,
    oldFeature: Feature<Geometry, GeoJsonProperties>
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
        ? this.getPointSymbolDiff(newFeature, oldFeature)
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

  private getPointSymbolDiff(
    newFeature: Feature<Geometry, GeoJsonProperties>,
    oldFeature: Feature<Geometry, GeoJsonProperties>
  ): PointSymbolDiff {
    const newGeometry = newFeature.geometry as Point;
    const oldGeometry = oldFeature.geometry as Point;
    const movementInMeters = getDistance(
      {
        lat: newGeometry.coordinates[0],
        lon: newGeometry.coordinates[1],
      },
      { lat: oldGeometry.coordinates[0], lon: oldGeometry.coordinates[1] }
    );
    return { movementInMeters };
  }

  private convertToDeletedSymbol(
    feature: Feature<Geometry, GeoJsonProperties>
  ): DeletedSymbolDto {
    const symbolName: string = IofSymbolHelper.getSymbolName(
      feature.properties?.[OcadPropertyKeys.Symbol]
    );
    const symbolNumber: string = IofSymbolHelper.getSymbolNumber(
      feature.properties?.[OcadPropertyKeys.Symbol]
    );
    return {
      createdAtUtc: this.getDateFromExcelDate(
        feature.properties?.[OcadPropertyKeys.CreationDate]
      ),
      lastEditedAtUtc: this.getDateFromExcelDate(
        feature.properties?.[OcadPropertyKeys.ModificationDate]
      ),
      symbolName,
      symbolNumber,
    };
  }
}

export enum OcadPropertyKeys {
  CreationDate = 'creationDate',
  ModificationDate = 'modificationDate',
  Symbol = 'sym',
  ParentId = 'parentId',
}
