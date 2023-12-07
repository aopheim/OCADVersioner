import { isNil } from 'lodash-es';

export class OcadDirectoryHelper {
  public static isValidReleaseFolderName(name?: string): boolean {
    if (isNil(name) || name.length === 0) return false;
    const possibleVersionNumber = +name.slice(1);
    if (
      name.length >= 2 &&
      name.startsWith('V', 0) &&
      !isNaN(possibleVersionNumber) &&
      +possibleVersionNumber > 0
    ) {
      return true;
    }
    return false;
  }

  public static getOcdReleaseFileName(versionName: string): string {
    return `${versionName}.ocd`;
  }

  public static getJsonMetadataFileName(versionName: string): string {
    return `${versionName}.json`;
  }

  public static getVersionNumberFromVersionName(
    versionName: string
  ): number | null {
    const potentialNumber = versionName.replace('V', '');
    return !isNaN(+potentialNumber) && +potentialNumber > 0
      ? +potentialNumber
      : null;
  }

  public static getVersionNameFromVersionNumber(versionNumber: number) {
    if (versionNumber === 0) return 'current';
    return `V${versionNumber}`;
  }
}
