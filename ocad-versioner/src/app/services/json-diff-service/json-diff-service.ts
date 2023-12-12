import { Injectable } from '@angular/core';
import { IJsonDiffService } from './json-diff-service.models';
import {
  AddedSymbolDto,
  DeletedSymbolDto,
  EditedSymbolDto,
  OcadDiffDto,
} from '../../components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models';
import { isNil, isEqual } from 'lodash-es';
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

@Injectable()
export class JsonDiffService implements IJsonDiffService {
  // Interesting... https://knowledge.broadcom.com/external/article/57052/how-to-convert-unix-epoch-time-values-in.html
  private readonly ExcelEpoch: number = 25569;

  getJsonDiff(
    oldFeatures: FeatureCollection,
    newFeatures: FeatureCollection
  ): OcadDiffDto {
    const added: AddedSymbolDto[] = [];
    const edited: EditedSymbolDto[] = [];
    const deleted: DeletedSymbolDto[] = [];
    newFeatures.features.forEach((newFeature) => {
      const indexInOldFeatures = oldFeatures.features.findIndex(
        (f) => f.id === newFeature.id
      );
      const matchInOldFeatures =
        indexInOldFeatures > -1
          ? oldFeatures.features[indexInOldFeatures]
          : null;
      if (isNil(matchInOldFeatures)) {
        if (!this.isChildFeature(newFeature))
          added.push(this.convertToAddedSymbol(newFeature));
      } else {
        if (!this.areFeaturesEqual(newFeature, matchInOldFeatures))
          if (!this.isChildFeature(newFeature))
            edited.push(this.convertToEditedSymbol(newFeature));
        // The feature is either edited or untouched. Gives fewer possible deleted elements to search for.
        oldFeatures.features.splice(indexInOldFeatures, 1);
      }
    });
    oldFeatures.features.forEach((oldFeature) => {
      const indexInNewFeatures = newFeatures.features.findIndex(
        (f) => f.id === oldFeature.id
      );
      if (indexInNewFeatures > -1)
        deleted.push(this.convertToDeletedSymbol(oldFeature));
    });

    return {
      added,
      deleted,
      edited,
    } as OcadDiffDto;
  }

  isChildFeature(feature: Feature<Geometry, GeoJsonProperties>): boolean {
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
    feature: Feature<Geometry, GeoJsonProperties>
  ): EditedSymbolDto {
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
      areaSymbolDiff: null,
      lineSymbolDiff: null,
      pointSymbolDiff: null,
    };
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
