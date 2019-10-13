import { Link } from 'react-router-dom';
import * as React from 'react';
import * as Registration from './Registration';

import './Dependencies.css';

interface IDependenciesProps {
  dependencyGroups: Registration.IDependencyGroup[];
}

interface IPackageDependenciesProps {
  dependencies: Registration.IDependency[] | undefined;
}

class Dependencies extends React.Component<IDependenciesProps> {

  static readonly netFrameworkRegex : RegExp = /net[0-9]{2,3}$/;
  static readonly netCoreRegex : RegExp = /netcoreapp[0-9].[0-9]$/;
  static readonly netStandardRegex : RegExp = /netstandard[0-9].[0-9]$/;
  static readonly versionRangeRegex : RegExp = /\[[0-9](.[0-9])*, \)$/;

  constructor(props: IDependenciesProps) {
    super(props);

    props.dependencyGroups.forEach(Dependencies.prettifyDependencyGroup);
  }

  private static prettifyDependencyGroup(group: Registration.IDependencyGroup) {
    if (!group.dependencies) {
      group.dependencies = [];
    }

    Dependencies.prettifyTargetFramework(group);

    if (group.dependencies !== undefined) {
      group.dependencies.forEach(Dependencies.prettifyDepency);
    }
  }

  private static prettifyTargetFramework(group: Registration.IDependencyGroup) {
    // This uses heuristics and may produce incorrect results.
    // This ignores portable class libraries.
    if (Dependencies.netFrameworkRegex.test(group.targetFramework)) {
      const version = group.targetFramework.substring("net".length);
      const prettyVersion = version.length === 2
        ? `${version[0]}.${version[1]}`
        : `${version[0]}.${version[1]}.${version[2]}`;

      group.targetFramework = `.NET Framework ${prettyVersion}`;
      return;
    }

    if (Dependencies.netCoreRegex.test(group.targetFramework)) {
      const version = group.targetFramework.substring("netcoreapp".length);
      group.targetFramework = `.NET Core ${version}`;
      return;
    }

    if (Dependencies.netStandardRegex.test(group.targetFramework)) {
      const version = group.targetFramework.substring("netstandard".length);
      group.targetFramework = `.NET Standard ${version}`;
      return;
    }
  }

  private static prettifyDepency(dependency: Registration.IDependency) {
    // This uses heuristics and may produce incorrect results.
    if (Dependencies.versionRangeRegex.test(dependency.range)) {
      dependency.range = `(>= ${dependency.range.slice(1, -3)})`;
    }
  }

  public render() {
    if (this.props.dependencyGroups.length === 0) {
      return (
        <div>
          <h3>Dependencies</h3>

          <div>This package has no dependencies.</div>
        </div>
      );
    }

    return (
        <div>
          <h3>Dependencies</h3>

          <div className="dependency-groups">
            {this.props.dependencyGroups.map(group => (
              <div key={group.targetFramework}>
                <h4>
                  <span>{group.targetFramework}</span>
                </h4>

                <PackageDependencies dependencies={group.dependencies} />
              </div>
            ))}
          </div>
        </div>
    );
  }
}

// tslint:disable-next-line:max-classes-per-file
class PackageDependencies extends React.Component<IPackageDependenciesProps> {

  public render() {
    if (!this.props.dependencies || this.props.dependencies.length === 0) {
      return <div>No dependencies.</div>
    }

    return (
      <ul className="list-unstyled dependency-group">
        {this.props.dependencies.map(dependency => (
          <li key={dependency.id}>
            <Link to={`/packages/${dependency.id}`}>{dependency.id}</Link>

            <span> {dependency.range}</span>
          </li>
        ))}
      </ul>
    );
  }
}

export default Dependencies;
