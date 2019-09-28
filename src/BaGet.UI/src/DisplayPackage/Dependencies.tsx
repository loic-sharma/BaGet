import * as React from 'react';
import * as Registration from './Registration';

interface IDependenciesProps {
  dependencyGroups: Registration.IDependencyGroup[];
}

interface IPackageDependenciesProps {
  dependencies: Registration.IDependency[] | undefined;
}

class Dependencies extends React.Component<IDependenciesProps> {

  constructor(props: IDependenciesProps) {
    props.dependencyGroups.forEach(group => {
      if (!group.dependencies) {
        group.dependencies = [];
      }
    });

    super(props);
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

          <div>
            {this.props.dependencyGroups.map(group => (
              <div key={group.targetFramework}>
                <h4>{group.targetFramework}</h4>

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
      <ul>
        {this.props.dependencies.map(dependency => (
          <li key={dependency.id}>
            {dependency.id} {dependency.range}
          </li>
        ))}
      </ul>
    );
  }
}

export default Dependencies;
